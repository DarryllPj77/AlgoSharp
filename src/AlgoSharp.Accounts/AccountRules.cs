using Microsoft.AspNetCore.Identity;

namespace AlgoSharp.Accounts;

/// <summary>Account policy shared by Identity's configuration, the forms and the tests.</summary>
public static class AccountRules
{
    public const int UserNameMin = 3;
    public const int UserNameMax = 32;
    public const int PasswordMin = 8;
    public const int MaxFailedSignIns = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public const string UserNameChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_.-";
    public const string UserNameRule = "Usernames are 3–32 characters: letters, digits, '_', '.' or '-'.";

    /// <summary>Returns a message if the username has the wrong shape (uniqueness is checked by Identity).</summary>
    public static string? CheckUserName(string userName)
    {
        if (userName.Length is < UserNameMin or > UserNameMax) return UserNameRule;
        if (userName.Any(c => !UserNameChars.Contains(c))) return UserNameRule;
        if (userName.Contains('@')) return "Usernames can't contain '@' (that's how sign-in tells them apart from emails).";
        return null;
    }

    public static void Apply(IdentityOptions o)
    {
        o.User.RequireUniqueEmail = true;
        o.User.AllowedUserNameCharacters = UserNameChars;

        o.Password.RequiredLength = PasswordMin;
        o.Password.RequireDigit = true;
        o.Password.RequireLowercase = true;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequiredUniqueChars = 4;

        o.Lockout.AllowedForNewUsers = true;
        o.Lockout.MaxFailedAccessAttempts = MaxFailedSignIns;
        o.Lockout.DefaultLockoutTimeSpan = LockoutDuration;

        // No email verification yet: dummy-but-valid emails are accepted, so don't require confirmation.
        o.SignIn.RequireConfirmedEmail = false;
        o.SignIn.RequireConfirmedAccount = false;
    }
}
