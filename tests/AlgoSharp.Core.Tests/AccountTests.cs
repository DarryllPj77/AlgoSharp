using System.Security.Claims;
using AlgoSharp.Accounts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AlgoSharp.Core.Tests;

public class AccountHelperTests
{
    [Fact]
    public void Neon_uri_becomes_key_value_with_tls()
    {
        string cs = DbConnection.Normalize("postgresql://alice:p%40ss%3Aw0rd@ep-cool-123.eu-central-1.aws.neon.tech/algosharp?sslmode=require&channel_binding=require");
        var b = new Npgsql.NpgsqlConnectionStringBuilder(cs);
        Assert.Equal("ep-cool-123.eu-central-1.aws.neon.tech", b.Host);
        Assert.Equal("algosharp", b.Database);
        Assert.Equal("alice", b.Username);
        Assert.Equal("p@ss:w0rd", b.Password);
        Assert.Equal(Npgsql.SslMode.Require, b.SslMode);
        Assert.Equal(5432, b.Port);
    }

    [Theory]
    [InlineData("postgres://u:p@db.example.com/app")]
    [InlineData("postgres://u:p@db.example.com/app?sslmode=disable")]
    [InlineData("Host=db.example.com;Database=app;Username=u;Password=p;SSL Mode=Prefer")]
    public void Remote_hosts_always_get_tls(string raw)
    {
        var b = new Npgsql.NpgsqlConnectionStringBuilder(DbConnection.Normalize(raw));
        Assert.Equal(Npgsql.SslMode.Require, b.SslMode);
    }

    [Fact]
    public void Localhost_may_skip_tls()
    {
        var b = new Npgsql.NpgsqlConnectionStringBuilder(DbConnection.Normalize("Host=localhost;Database=app;Username=u;Password=p;SSL Mode=Disable"));
        Assert.Equal(Npgsql.SslMode.Disable, b.SslMode);
    }

    [Fact]
    public void Describe_never_includes_the_password()
    {
        string text = DbConnection.Describe(DbConnection.Normalize("postgresql://alice:hunter2secret@host.neon.tech/db"));
        Assert.DoesNotContain("hunter2secret", text);
        Assert.DoesNotContain("alice", text);
        Assert.Contains("host.neon.tech", text);
    }

    [Theory]
    [InlineData("/problems/mine", "/problems/mine")]
    [InlineData("/problems?x=1#top", "/problems?x=1#top")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("/ok\\..\\x", "/")]
    [InlineData("javascript:alert(1)", "/")]
    [InlineData("problems", "/")]
    public void Return_urls_stay_on_this_site(string? input, string expected) => Assert.Equal(expected, ReturnUrl.Safe(input));

    [Theory]
    [InlineData("/problems/two-sum", true)]
    [InlineData("/problems/mine", true)]
    [InlineData("/problems/mine/abc123", true)]
    [InlineData("/problems/import", true)]
    [InlineData("/code/two-sum", true)]
    [InlineData("/problems", false)]
    [InlineData("/visualize/bubble-sort", false)]
    [InlineData("/account", false)]
    [InlineData("//evil.example/problems/x", false)]
    [InlineData(null, false)]
    public void Workspace_paths_get_the_sign_in_message(string? url, bool expected) => Assert.Equal(expected, ReturnUrl.IsWorkspace(url));

    [Theory]
    [InlineData("ada", true)]
    [InlineData("Ada_Lovelace-1.0", true)]
    [InlineData("ab", false)]
    [InlineData("has space", false)]
    [InlineData("name@site", false)]
    [InlineData("émile", false)]
    public void Username_shape(string name, bool ok) => Assert.Equal(ok, AccountRules.CheckUserName(name) is null);
}

/// <summary>
/// Exercises the real Identity stack (password hashing, lockout, security stamps, cookie sign-in)
/// against an in-memory SQLite database.
/// </summary>
public sealed class AccountServiceTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ServiceProvider root;

    public AccountServiceTests()
    {
        connection.Open();
        root = BuildServices(o => o.UseSqlite(connection), enabled: true);
        using var scope = root.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    private static ServiceProvider BuildServices(Action<DbContextOptionsBuilder> db, bool enabled)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestEnv());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddHttpContextAccessor();
        services.AddSingleton(new AccountsStatus(enabled));
        services.AddDbContext<AppDbContext>(db);
        services.AddAuthentication(o =>
            {
                o.DefaultScheme = IdentityConstants.ApplicationScheme;
                o.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();
        services.AddIdentityCore<AppUser>(AccountRules.Apply)
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
        services.AddScoped<AccountService>();
        return services.BuildServiceProvider();
    }

    private sealed class TestEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "AlgoSharp.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>One "request": a scope plus an HttpContext whose response receives the auth cookie.</summary>
    private (AccountService Accounts, HttpContext Http, IServiceScope Scope) Request(ServiceProvider? sp = null)
    {
        var scope = (sp ?? root).CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("algosharp.test");
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
        return (scope.ServiceProvider.GetRequiredService<AccountService>(), http, scope);
    }

    private async Task<ClaimsPrincipal> PrincipalFor(string userName)
    {
        using var scope = root.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var factory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<AppUser>>();
        return await factory.CreateAsync((await users.FindByNameAsync(userName))!);
    }

    private AppUser? Load(string userName)
    {
        using var scope = root.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleOrDefault(u => u.NormalizedUserName == userName.ToUpperInvariant());
    }

    private async Task Register(string user = "ada", string email = "ada@example.com", string password = "correct-horse-1")
    {
        var (accounts, _, scope) = Request();
        using (scope) Assert.True((await accounts.RegisterAsync(user, email, password)).Succeeded);
    }

    [Fact]
    public async Task Registration_stores_only_a_password_hash()
    {
        await Register();
        var user = Load("ada")!;
        Assert.NotNull(user.PasswordHash);
        Assert.DoesNotContain("correct-horse-1", user.PasswordHash);
        Assert.True(user.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal("ada@example.com", user.Email);
        Assert.False(user.EmailConfirmed); // no verification flow yet, and none is faked
    }

    [Fact]
    public async Task Usernames_and_emails_are_unique_case_insensitively()
    {
        await Register();
        var (accounts, _, scope) = Request();
        using (scope)
        {
            var sameName = await accounts.RegisterAsync("ADA", "other@example.com", "correct-horse-1");
            Assert.False(sameName.Succeeded);
            Assert.Contains(sameName.Errors, e => e.Field == "UserName");

            var sameEmail = await accounts.RegisterAsync("grace", "ADA@example.com", "correct-horse-1");
            Assert.False(sameEmail.Succeeded);
            Assert.Contains(sameEmail.Errors, e => e.Field == "Email");
        }
    }

    [Theory]
    [InlineData("short1")]
    [InlineData("nodigitshere")]
    [InlineData("12345678")]
    public async Task Weak_passwords_are_rejected_on_the_password_field(string password)
    {
        var (accounts, _, scope) = Request();
        using (scope)
        {
            var r = await accounts.RegisterAsync("grace", "grace@example.com", password);
            Assert.False(r.Succeeded);
            Assert.All(r.Errors, e => Assert.Equal("Password", e.Field));
        }
        Assert.Null(Load("grace"));
    }

    [Fact]
    public async Task Sign_in_works_with_username_or_email()
    {
        await Register();
        foreach (string login in new[] { "ada", "ADA", "ada@example.com", " Ada@Example.com " })
        {
            var (accounts, http, scope) = Request();
            using (scope)
            {
                Assert.Equal(SignInOutcome.Succeeded, await accounts.SignInAsync(login, "correct-horse-1", remember: false));
                Assert.Contains(".AspNetCore.Identity.Application=", http.Response.Headers.SetCookie.ToString());
            }
        }
        Assert.NotNull(Load("ada")!.LastSignInAt);
    }

    [Fact]
    public async Task Cookie_is_persistent_only_when_requested()
    {
        await Register();
        var (a1, h1, s1) = Request();
        using (s1) await a1.SignInAsync("ada", "correct-horse-1", remember: false);
        var (a2, h2, s2) = Request();
        using (s2) await a2.SignInAsync("ada", "correct-horse-1", remember: true);

        string session = h1.Response.Headers.SetCookie.ToString(), persistent = h2.Response.Headers.SetCookie.ToString();
        Assert.DoesNotContain("expires=", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", persistent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", persistent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_accounts_and_wrong_passwords_fail_the_same_way()
    {
        await Register();
        var (accounts, http, scope) = Request();
        using (scope)
        {
            Assert.Equal(SignInOutcome.Failed, await accounts.SignInAsync("nobody", "correct-horse-1", false));
            Assert.Equal(SignInOutcome.Failed, await accounts.SignInAsync("ada", "wrong-password-1", false));
            Assert.Equal(0, http.Response.Headers.SetCookie.Count);
        }
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account()
    {
        await Register();
        for (int i = 0; i < AccountRules.MaxFailedSignIns - 1; i++)
        {
            var (a, _, s) = Request();
            using (s) Assert.Equal(SignInOutcome.Failed, await a.SignInAsync("ada", "wrong-password-1", false));
        }
        var (accounts, _, scope) = Request();
        using (scope)
        {
            Assert.Equal(SignInOutcome.LockedOut, await accounts.SignInAsync("ada", "wrong-password-1", false));
            // Even the right password is refused while locked.
            Assert.Equal(SignInOutcome.LockedOut, await accounts.SignInAsync("ada", "correct-horse-1", false));
        }
    }

    [Fact]
    public async Task Changing_the_password_requires_the_current_one_and_rotates_the_stamp()
    {
        await Register();
        var principal = await PrincipalFor("ada");
        string? stampBefore = Load("ada")!.SecurityStamp;

        var (a1, _, s1) = Request();
        using (s1)
        {
            var wrong = await a1.ChangePasswordAsync(principal, "not-my-password-1", "brand-new-pass-2");
            Assert.False(wrong.Succeeded);
            Assert.Contains(wrong.Errors, e => e.Field == "CurrentPassword");
        }

        var (a2, _, s2) = Request();
        using (s2) Assert.True((await a2.ChangePasswordAsync(principal, "correct-horse-1", "brand-new-pass-2")).Succeeded);

        var after = Load("ada")!;
        Assert.NotEqual(stampBefore, after.SecurityStamp);
        Assert.NotNull(after.PasswordChangedAt);

        var (a3, _, s3) = Request();
        using (s3)
        {
            Assert.Equal(SignInOutcome.Failed, await a3.SignInAsync("ada", "correct-horse-1", false));
            Assert.Equal(SignInOutcome.Succeeded, await a3.SignInAsync("ada", "brand-new-pass-2", false));
        }
    }

    [Fact]
    public async Task Guessing_the_current_password_on_change_password_locks_the_account()
    {
        await Register();
        var principal = await PrincipalFor("ada");
        for (int i = 0; i < AccountRules.MaxFailedSignIns; i++)
        {
            var (a, _, s) = Request();
            using (s) Assert.False((await a.ChangePasswordAsync(principal, $"guess-number-{i}", "brand-new-pass-2")).Succeeded);
        }
        var (accounts, _, scope) = Request();
        using (scope)
        {
            var r = await accounts.ChangePasswordAsync(principal, "correct-horse-1", "brand-new-pass-2");
            Assert.Contains("locked", Assert.Single(r.Errors).Message);
            Assert.Equal(SignInOutcome.LockedOut, await accounts.SignInAsync("ada", "correct-horse-1", false));
        }
    }

    [Fact]
    public async Task Profile_changes_need_reauthentication_and_stay_unique()
    {
        await Register();
        await Register("grace", "grace@example.com");
        var principal = await PrincipalFor("ada");

        var (a1, _, s1) = Request();
        using (s1)
        {
            var noPassword = await a1.UpdateProfileAsync(principal, "ada2", "ada2@example.com", "wrong-password-1");
            Assert.False(noPassword.Succeeded);
            Assert.Contains(noPassword.Errors, e => e.Field == "CurrentPassword");
            Assert.NotNull(Load("ada"));

            var taken = await a1.UpdateProfileAsync(principal, "grace", "ada@example.com", "correct-horse-1");
            Assert.False(taken.Succeeded);
            Assert.Contains(taken.Errors, e => e.Field == "UserName");
        }

        var (a2, _, s2) = Request();
        using (s2) Assert.True((await a2.UpdateProfileAsync(principal, "ada.lovelace", "ada@analytical.engine", "correct-horse-1")).Succeeded);
        Assert.Null(Load("ada"));
        Assert.Equal("ada@analytical.engine", Load("ada.lovelace")!.Email);
    }

    [Fact]
    public async Task Sign_out_everywhere_invalidates_existing_sessions()
    {
        await Register();
        var principal = await PrincipalFor("ada");
        string? stamp = Load("ada")!.SecurityStamp;

        var (a1, _, s1) = Request();
        using (s1) Assert.False((await a1.SignOutEverywhereAsync(principal, "wrong-password-1")).Succeeded);
        Assert.Equal(stamp, Load("ada")!.SecurityStamp);

        var (a2, _, s2) = Request();
        using (s2) Assert.True((await a2.SignOutEverywhereAsync(principal, "correct-horse-1")).Succeeded);
        Assert.NotEqual(stamp, Load("ada")!.SecurityStamp);
    }

    [Fact]
    public async Task Disabled_accounts_explain_themselves()
    {
        using var sp = BuildServices(o => o.UseSqlite(connection), enabled: false);
        var (accounts, _, scope) = Request(sp);
        using (scope)
        {
            var r = await accounts.RegisterAsync("ada", "ada@example.com", "correct-horse-1");
            Assert.Equal(AccountService.NotConfigured, Assert.Single(r.Errors).Message);
            Assert.Equal(SignInOutcome.Unavailable, await accounts.SignInAsync("ada", "correct-horse-1", false));
        }
    }

    [Fact]
    public async Task Unreachable_database_is_reported_not_thrown()
    {
        // Nothing listens on port 1: the connection is refused immediately.
        using var sp = BuildServices(o => o.UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2"), enabled: true);
        var (accounts, _, scope) = Request(sp);
        using (scope)
        {
            Assert.Equal(SignInOutcome.Unavailable, await accounts.SignInAsync("ada", "correct-horse-1", false));
            var r = await accounts.RegisterAsync("ada", "ada@example.com", "correct-horse-1");
            Assert.Equal(AccountService.Unavailable, Assert.Single(r.Errors).Message);
        }
    }

    [Fact]
    public void Production_cookie_setup_keeps_the_security_stamp_check()
    {
        // Regression: customizing cookie events must not drop Identity's OnValidatePrincipal, or a password
        // change / "sign out everywhere" never reaches other devices.
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Accounts:SessionRecheckSeconds"] = "60" }).Build();
        services.AddAlgoSharpAccounts(config, new TestEnv { EnvironmentName = "Production" });
        using var sp = services.BuildServiceProvider();

        var cookie = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        Assert.Equal(typeof(SecurityStampValidator), cookie.Events.OnValidatePrincipal.Method.DeclaringType);
        Assert.Equal(Microsoft.AspNetCore.Http.CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.Equal(TimeSpan.FromSeconds(60), sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SecurityStampValidatorOptions>>().Value.ValidationInterval);
    }

    [Fact]
    public void Key_ring_is_encrypted_with_the_configured_certificate()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=AlgoSharp DP test", rsa,
            System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        string pfx = Convert.ToBase64String(cert.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, "test-pass"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:Store"] = "Database",
            ["DataProtection:CertificateBase64"] = pfx,
            ["DataProtection:CertificatePassword"] = "test-pass",
        }).Build();

        ServiceProvider KeyRingServices(IConfiguration cfg)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
            AccountsSetup.ConfigureKeyRing(Microsoft.Extensions.DependencyInjection.DataProtectionServiceCollectionExtensions.AddDataProtection(services), cfg, databaseAvailable: true);
            return services.BuildServiceProvider();
        }

        string token;
        using (var sp = KeyRingServices(config))
        {
            token = sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>().CreateProtector("t").Protect("secret-cookie");
        }

        using (var scope = root.CreateScope())
        {
            string xml = scope.ServiceProvider.GetRequiredService<AppDbContext>().DataProtectionKeys.AsNoTracking().Single().Xml!;
            Assert.Contains("EncryptedData", xml);
            Assert.DoesNotContain("<masterKey", xml);
        }

        // A fresh app instance with the same certificate can read cookies issued earlier.
        using (var sp = KeyRingServices(config))
            Assert.Equal("secret-cookie", sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>().CreateProtector("t").Unprotect(token));

        // Someone with only the database (no certificate) can't.
        var noCert = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataProtection:Store"] = "Database" }).Build();
        using (var sp = KeyRingServices(noCert))
            Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() =>
                sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>().CreateProtector("t").Unprotect(token));
    }

    public void Dispose()
    {
        root.Dispose();
        connection.Dispose();
    }
}
