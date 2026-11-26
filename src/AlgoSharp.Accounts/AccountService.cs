using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlgoSharp.Accounts;

/// <summary>Whether accounts are available in this deployment (a database is configured).</summary>
public sealed record AccountsStatus(bool Enabled, string? Reason = null);

/// <summary>An error for one form field ("" = the whole form).</summary>
public sealed record FieldError(string Field, string Message);

public sealed record AccountResult(bool Succeeded, IReadOnlyList<FieldError> Errors)
{
    public static readonly AccountResult Ok = new(true, []);

    public static AccountResult Fail(string message, string field = "") => new(false, [new FieldError(field, message)]);

    public IEnumerable<string> For(string field) => Errors.Where(e => e.Field == field).Select(e => e.Message);
}

public enum SignInOutcome { Succeeded, Failed, LockedOut, NotAllowed, Unavailable }

/// <summary>
/// Account operations used by the account pages. Passwords are only ever handed to Identity,
/// which stores a salted PBKDF2 hash; nothing here logs or returns them.
/// </summary>
public sealed class AccountService(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    AccountsStatus status,
    ILogger<AccountService> log)
{
    public const string Unavailable = "Accounts are temporarily unavailable (the account database can't be reached). Your local library still works. Please try again in a minute.";
    public const string NotConfigured = "Accounts aren't set up in this deployment yet. Everything else, including your local problem library, works without signing in.";
    public const string BadCredentials = "That username/email and password don't match an account.";

    public bool Enabled => status.Enabled;

    public async Task<AccountResult> RegisterAsync(string userName, string email, string password, CancellationToken ct = default)
    {
        if (!status.Enabled) return AccountResult.Fail(NotConfigured);
        userName = userName.Trim();
        email = email.Trim();
        var shape = AccountRules.CheckUserName(userName);
        if (shape is not null) return AccountResult.Fail(shape, "UserName");

        var user = new AppUser { UserName = userName, Email = email, CreatedAt = DateTimeOffset.UtcNow, PasswordChangedAt = DateTimeOffset.UtcNow };
        try
        {
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded) return FromIdentity(result);
            log.LogInformation("Account {UserId} registered", user.Id);
            return AccountResult.Ok;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Lost a race with a simultaneous sign-up: the database's unique indexes caught it.
            return AccountResult.Fail("That username or email is already registered.");
        }
        catch (Exception ex) when (IsDatabaseFailure(ex))
        {
            log.LogWarning("Registration failed: database unavailable ({Error})", ex.GetType().Name);
            return AccountResult.Fail(Unavailable);
        }
    }

    /// <summary>Signs in by username or email. The cookie is persistent only when <paramref name="remember"/> is set.</summary>
    public async Task<SignInOutcome> SignInAsync(string login, string password, bool remember)
    {
        if (!status.Enabled) return SignInOutcome.Unavailable;
        try
        {
            var user = await FindByLoginAsync(login);
            if (user is null)
            {
                // Spend about as long as a real check so response time doesn't reveal which accounts exist.
                users.PasswordHasher.VerifyHashedPassword(new AppUser(), DummyHash, password);
                return SignInOutcome.Failed;
            }
            var result = await signIn.PasswordSignInAsync(user, password, remember, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                user.LastSignInAt = DateTimeOffset.UtcNow;
                await users.UpdateAsync(user);
                log.LogInformation("Account {UserId} signed in", user.Id);
                return SignInOutcome.Succeeded;
            }
            if (result.IsLockedOut)
            {
                log.LogWarning("Account {UserId} is locked out after repeated failed sign-ins", user.Id);
                return SignInOutcome.LockedOut;
            }
            return result.IsNotAllowed ? SignInOutcome.NotAllowed : SignInOutcome.Failed;
        }
        catch (Exception ex) when (IsDatabaseFailure(ex))
        {
            log.LogWarning("Sign-in failed: database unavailable ({Error})", ex.GetType().Name);
            return SignInOutcome.Unavailable;
        }
    }

    public async Task<AppUser?> FindByLoginAsync(string login)
    {
        login = login.Trim();
        if (login.Length == 0) return null;
        return await users.FindByNameAsync(login)
               ?? (login.Contains('@') ? await users.FindByEmailAsync(login) : null);
    }

    public Task<AppUser?> CurrentAsync(ClaimsPrincipal principal) =>
        status.Enabled ? users.GetUserAsync(principal) : Task.FromResult<AppUser?>(null);

    public async Task<AccountResult> ChangePasswordAsync(ClaimsPrincipal principal, string currentPassword, string newPassword)
    {
        return await Guarded(principal, async user =>
        {
            // UserManager.ChangePasswordAsync doesn't count wrong passwords toward lockout, so check
            // first through the sign-in manager: guessing via this form locks the account like sign-in does.
            var check = await signIn.CheckPasswordSignInAsync(user, currentPassword, lockoutOnFailure: true);
            if (check.IsLockedOut) return AccountResult.Fail("Too many incorrect passwords. Your account is locked for a few minutes.", "CurrentPassword");
            if (!check.Succeeded) return AccountResult.Fail("Your current password is incorrect.", "CurrentPassword");

            var result = await users.ChangePasswordAsync(user, currentPassword, newPassword);
            if (!result.Succeeded)
            {
                return result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch))
                    ? AccountResult.Fail("Your current password is incorrect.", "CurrentPassword")
                    : FromIdentity(result, passwordField: "NewPassword");
            }
            user.PasswordChangedAt = DateTimeOffset.UtcNow;
            await users.UpdateAsync(user);
            // ChangePasswordAsync rotated the security stamp, which signs out every other device;
            // re-issue this browser's cookie so the person stays signed in here.
            await signIn.RefreshSignInAsync(user);
            log.LogInformation("Account {UserId} changed password", user.Id);
            return AccountResult.Ok;
        });
    }

    /// <summary>Changes the username and/or email. Requires the current password (reauthentication).</summary>
    public async Task<AccountResult> UpdateProfileAsync(ClaimsPrincipal principal, string userName, string email, string currentPassword)
    {
        return await Guarded(principal, async user =>
        {
            userName = userName.Trim();
            email = email.Trim();
            var shape = AccountRules.CheckUserName(userName);
            if (shape is not null) return AccountResult.Fail(shape, "UserName");

            var check = await signIn.CheckPasswordSignInAsync(user, currentPassword, lockoutOnFailure: true);
            if (check.IsLockedOut) return AccountResult.Fail("Too many incorrect passwords. Your account is locked for a few minutes.", "CurrentPassword");
            if (!check.Succeeded) return AccountResult.Fail("Your current password is incorrect.", "CurrentPassword");

            bool changed = false;
            if (!string.Equals(user.UserName, userName, StringComparison.Ordinal))
            {
                var r = await users.SetUserNameAsync(user, userName);
                if (!r.Succeeded) return FromIdentity(r);
                changed = true;
            }
            if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                var r = await users.SetEmailAsync(user, email);
                if (!r.Succeeded) return FromIdentity(r);
                changed = true;
            }
            if (!changed) return AccountResult.Fail("Nothing to save: the username and email are unchanged.");
            await signIn.RefreshSignInAsync(user);
            log.LogInformation("Account {UserId} updated its profile", user.Id);
            return AccountResult.Ok;
        });
    }

    /// <summary>Invalidates every existing sign-in cookie for this account (all devices), then signs out here.</summary>
    public async Task<AccountResult> SignOutEverywhereAsync(ClaimsPrincipal principal, string currentPassword)
    {
        return await Guarded(principal, async user =>
        {
            var check = await signIn.CheckPasswordSignInAsync(user, currentPassword, lockoutOnFailure: true);
            if (check.IsLockedOut) return AccountResult.Fail("Too many incorrect passwords. Your account is locked for a few minutes.", "CurrentPassword");
            if (!check.Succeeded) return AccountResult.Fail("Your current password is incorrect.", "CurrentPassword");
            await users.UpdateSecurityStampAsync(user);
            await signIn.SignOutAsync();
            log.LogInformation("Account {UserId} signed out of all devices", user.Id);
            return AccountResult.Ok;
        });
    }

    public Task SignOutAsync() => signIn.SignOutAsync();

    private async Task<AccountResult> Guarded(ClaimsPrincipal principal, Func<AppUser, Task<AccountResult>> work)
    {
        if (!status.Enabled) return AccountResult.Fail(NotConfigured);
        try
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return AccountResult.Fail("Your session has ended. Please sign in again.");
            return await work(user);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return AccountResult.Fail("That username or email is already in use.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return AccountResult.Fail("Your account was changed in another tab. Reload and try again.");
        }
        catch (Exception ex) when (IsDatabaseFailure(ex))
        {
            log.LogWarning("Account update failed: database unavailable ({Error})", ex.GetType().Name);
            return AccountResult.Fail(Unavailable);
        }
    }

    private static AccountResult FromIdentity(IdentityResult result, string passwordField = "Password") =>
        new(false, result.Errors.Select(e => new FieldError(FieldFor(e.Code, passwordField), Friendly(e))).ToList());

    private static string FieldFor(string code, string passwordField) => code switch
    {
        _ when code.StartsWith("Password", StringComparison.Ordinal) => passwordField,
        nameof(IdentityErrorDescriber.DuplicateUserName) or nameof(IdentityErrorDescriber.InvalidUserName) => "UserName",
        nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.InvalidEmail) => "Email",
        _ => "",
    };

    private static string Friendly(IdentityError e) => e.Code switch
    {
        nameof(IdentityErrorDescriber.DuplicateUserName) => "That username is taken.",
        nameof(IdentityErrorDescriber.DuplicateEmail) => "An account with that email already exists.",
        nameof(IdentityErrorDescriber.InvalidUserName) => AccountRules.UserNameRule,
        _ => e.Description,
    };

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is DbException { SqlState: "23505" } // PostgreSQL unique_violation
        || ex.InnerException?.Message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase) == true; // SQLite (tests)

    /// <summary>Connection, timeout and provider failures: shown as "temporarily unavailable" instead of a crash.</summary>
    public static bool IsDatabaseFailure(Exception ex) => ex switch
    {
        OperationCanceledException => false,
        DbUpdateException => false,
        DbException or TimeoutException or System.Net.Sockets.SocketException => true,
        InvalidOperationException when ex.InnerException is not null => IsDatabaseFailure(ex.InnerException),
        // "The ConnectionString property has not been initialized" or a transient-failure wrapper.
        InvalidOperationException => ex.Message.Contains("onnection", StringComparison.Ordinal) || ex.Message.Contains("transient", StringComparison.OrdinalIgnoreCase),
        _ => ex.InnerException is not null && IsDatabaseFailure(ex.InnerException),
    };

    // PBKDF2 hash of a random value, used only to equalize timing for unknown accounts.
    private static readonly string DummyHash = new PasswordHasher<AppUser>().HashPassword(new AppUser(), Guid.NewGuid().ToString());
}
