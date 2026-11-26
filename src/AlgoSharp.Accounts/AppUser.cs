using Microsoft.AspNetCore.Identity;

namespace AlgoSharp.Accounts;

/// <summary>An AlgoSharp account. Identity stores the password hash, security stamp and lockout state.</summary>
public sealed class AppUser : IdentityUser
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastSignInAt { get; set; }

    public DateTimeOffset? PasswordChangedAt { get; set; }
}
