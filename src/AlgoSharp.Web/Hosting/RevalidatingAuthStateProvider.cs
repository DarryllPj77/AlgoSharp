using System.Security.Claims;
using AlgoSharp.Accounts;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AlgoSharp.Web.Hosting;

/// <summary>
/// Interactive pages keep a long-lived circuit; this re-checks the account's security stamp every
/// few minutes so a password change or "sign out everywhere" also ends sessions in open tabs.
/// </summary>
internal sealed class RevalidatingAuthStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityOptions> options,
    AccountsStatus status)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(5);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken ct)
    {
        if (!status.Enabled) return false;
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        try
        {
            var user = await users.GetUserAsync(state.User);
            if (user is null) return false;
            if (!users.SupportsUserSecurityStamp) return true;
            string? principalStamp = state.User.FindFirstValue(options.Value.ClaimsIdentity.SecurityStampClaimType);
            return principalStamp == await users.GetSecurityStampAsync(user);
        }
        catch (Exception ex) when (AccountService.IsDatabaseFailure(ex))
        {
            // Don't sign people out of an open tab because the database blinked; check again next interval.
            return true;
        }
    }
}
