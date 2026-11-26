using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AlgoSharp.Accounts;

public static class AccountsSetup
{
    public const string ConnectionName = "AlgoSharp";

    /// <summary>
    /// The connection string comes from configuration only: the <c>ConnectionStrings__AlgoSharp</c>
    /// environment variable, user-secrets in development, or <c>DATABASE_URL</c> (a postgres:// URI).
    /// Nothing is read from files under source control.
    /// </summary>
    public static string? ConnectionString(IConfiguration config)
    {
        string? raw = config.GetConnectionString(ConnectionName);
        if (string.IsNullOrWhiteSpace(raw)) raw = config["DATABASE_URL"];
        return string.IsNullOrWhiteSpace(raw) ? null : DbConnection.Normalize(raw);
    }

    /// <summary>
    /// Registers Identity (cookie sign-in, lockout, password hashing) backed by PostgreSQL.
    /// Without a connection string the app still runs; account pages explain that accounts are off.
    /// </summary>
    /// <summary>
    /// Where the key ring lives and how it's protected at rest. With DataProtection:CertificateBase64
    /// (a PKCS#12 file, base64) and DataProtection:CertificatePassword, every key is encrypted with that
    /// certificate before it's stored, so a copy of the database or key folder alone can't be used to
    /// forge sign-in cookies. The certificate is managed separately from the database (an environment
    /// secret on the host). Returns whether keys are encrypted.
    /// </summary>
    public static bool ConfigureKeyRing(IDataProtectionBuilder dp, IConfiguration config, bool databaseAvailable)
    {
        dp.SetApplicationName("AlgoSharp");
        string? keysPath = config["DataProtection:KeysPath"];
        if (databaseAvailable && string.Equals(config["DataProtection:Store"], "Database", StringComparison.OrdinalIgnoreCase))
            dp.PersistKeysToDbContext<AppDbContext>();
        else if (!string.IsNullOrWhiteSpace(keysPath))
            dp.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        string? certificate = config["DataProtection:CertificateBase64"];
        if (string.IsNullOrWhiteSpace(certificate)) return false;
        var cert = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(certificate.Trim()), config["DataProtection:CertificatePassword"],
            X509KeyStorageFlags.EphemeralKeySet);
        if (!cert.HasPrivateKey) throw new InvalidOperationException("DataProtection:CertificateBase64 must include the private key.");
        // Decryption looks the certificate up by thumbprint in the OS store by default, which fails in
        // containers; hand it over directly instead.
        dp.ProtectKeysWithCertificate(cert).UnprotectKeysWithAnyCertificate(cert);
        return true;
    }

    public static AccountsStatus AddAlgoSharpAccounts(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        string? cs = ConnectionString(config);
        var status = cs is null
            ? new AccountsStatus(false, "No ConnectionStrings:AlgoSharp is configured.")
            : new AccountsStatus(true);
        services.AddSingleton(status);

        services.AddDbContext<AppDbContext>(o =>
        {
            if (cs is null) o.UseNpgsql();
            else o.UseNpgsql(cs, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null));
        });

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

        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "AlgoSharp.Auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            // Production cookies are HTTPS-only; development also allows plain http://localhost.
            o.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.LoginPath = "/account/login";
            o.LogoutPath = "/account/logout";
            o.AccessDeniedPath = "/account/login";
            o.ReturnUrlParameter = "returnUrl";
            // Used only when the person ticks "Keep me signed in"; otherwise the cookie ends with the browser session.
            o.ExpireTimeSpan = TimeSpan.FromDays(14);
            o.SlidingExpiration = true;
            // Only the redirect is customized: replacing the whole Events object would drop Identity's
            // OnValidatePrincipal (the security-stamp check that signs other devices out).
            // Sign-in pages are server-rendered forms, so a plain redirect is right for pages;
            // non-GET API-style calls get a 401 instead of an HTML redirect.
            o.Events.OnRedirectToLogin = ctx =>
            {
                if (!HttpMethods.IsGet(ctx.Request.Method)) ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                else ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            };
        });

        // How quickly a password change or "sign out everywhere" reaches other devices.
        services.Configure<SecurityStampValidatorOptions>(o =>
            o.ValidationInterval = TimeSpan.FromSeconds(Math.Clamp(config.GetValue("Accounts:SessionRecheckSeconds", 300), 10, 3600)));

        // Keys that encrypt the sign-in cookie, antiforgery tokens and Blazor circuit ids. They must
        // outlive container restarts: by default a mounted directory (DataProtection:KeysPath), which
        // keeps the site working even while the database is down. Multi-instance hosts can opt into
        // sharing them through the database instead (DataProtection:Store = "Database").
        ConfigureKeyRing(services.AddDataProtection(), config, status.Enabled);

        services.AddScoped<AccountService>();
        return status;
    }
}
