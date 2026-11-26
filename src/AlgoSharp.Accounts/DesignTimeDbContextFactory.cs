using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AlgoSharp.Accounts;

/// <summary>
/// Lets `dotnet ef migrations add` build the model without a database or the web host.
/// `dotnet ef database update` / `migrations script` read the real connection string from the
/// ALGOSHARP_DB environment variable (never from source control).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        string? configured = Environment.GetEnvironmentVariable("ALGOSHARP_DB")
                             ?? Environment.GetEnvironmentVariable("ConnectionStrings__AlgoSharp");
        var options = new DbContextOptionsBuilder<AppDbContext>();
        if (string.IsNullOrWhiteSpace(configured)) options.UseNpgsql();
        else options.UseNpgsql(DbConnection.Normalize(configured));
        return new AppDbContext(options.Options);
    }
}
