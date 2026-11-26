using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AlgoSharp.Accounts;

/// <summary>
/// Account data only: Identity tables plus the Data Protection key ring (so sign-in cookies
/// survive restarts and work across instances). The personal problem library is NOT stored
/// here: it stays in each browser's IndexedDB.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(user =>
        {
            // Identity only indexes emails non-uniquely; enforce uniqueness in the database too,
            // so two simultaneous sign-ups with the same email can't both succeed.
            user.HasIndex(u => u.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();
            user.Property(u => u.CreatedAt).IsRequired();
        });

        builder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys");
    }
}
