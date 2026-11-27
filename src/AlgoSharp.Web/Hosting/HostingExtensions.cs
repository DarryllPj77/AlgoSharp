using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using AlgoSharp.Accounts;
using AlgoSharp.Playground.Execution;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AlgoSharp.Web.Hosting;

public static class HostingExtensions
{
    public const string AccountPolicy = "account-forms";

    /// <summary>
    /// Limits account form posts (sign in, sign up, password changes) per client IP, on top of
    /// Identity's per-account lockout, to slow down password guessing and sign-up spam.
    /// </summary>
    public static IServiceCollection AddAccountRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                bool accountPost = HttpMethods.IsPost(ctx.Request.Method)
                                   && ctx.Request.Path.StartsWithSegments("/account", StringComparison.OrdinalIgnoreCase);
                if (!accountPost) return RateLimitPartition.GetNoLimiter("other");
                string ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
            o.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.Headers.RetryAfter = "60";
                ctx.HttpContext.Response.ContentType = "text/html; charset=utf-8";
                await ctx.HttpContext.Response.WriteAsync(
                    "<!DOCTYPE html><meta charset=utf-8><title>Too many attempts</title>" +
                    "<body style=\"font-family:system-ui;background:#0b0d12;color:#e7eaf0;display:grid;place-items:center;min-height:90vh\">" +
                    "<div style=\"max-width:420px;text-align:center\"><h1>Too many attempts</h1>" +
                    "<p>Please wait a minute, then <a style=\"color:#7aa2ff\" href=\"javascript:history.back()\">go back</a> and try again.</p></div>", ct);
            };
        });
        return services;
    }

    public static IServiceCollection AddAlgoSharpHealthChecks(this IServiceCollection services, AccountsStatus accounts)
    {
        var checks = services.AddHealthChecks();
        if (accounts.Enabled) checks.AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
        checks.AddCheck<SandboxHealthCheck>("sandbox", tags: ["ready"]);
        return services;
    }

    /// <summary>
    /// <c>/healthz</c>: the process is up (for container restarts). <c>/healthz/ready</c>: database and
    /// sandbox reachability (for load balancers). Responses carry statuses only, never connection details.
    /// </summary>
    public static void MapAlgoSharpHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/healthz", new() { Predicate = _ => false, ResponseWriter = WriteHealth }).DisableRateLimiting();
        app.MapHealthChecks("/healthz/ready", new()
        {
            Predicate = c => c.Tags.Contains("ready"),
            ResponseWriter = WriteHealth,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        }).DisableRateLimiting();
    }

    private static Task WriteHealth(HttpContext ctx, HealthReport report)
    {
        ctx.Response.ContentType = "application/json";
        ctx.Response.Headers.CacheControl = "no-store";
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => new { status = e.Value.Status.ToString(), description = e.Value.Description }),
        }));
    }

    /// <summary>
    /// Behind a reverse proxy (Caddy, nginx, a cloud load balancer) the app sees the proxy's IP and
    /// plain HTTP. When ReverseProxy:Enabled is true, X-Forwarded-For/Proto are trusted, but only from
    /// the configured proxies/networks, so clients can't spoof their IP to dodge rate limits.
    /// </summary>
    public static IServiceCollection AddReverseProxySupport(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection("ReverseProxy");
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 1;
            foreach (string ip in section.GetSection("KnownProxies").Get<string[]>() ?? [])
                o.KnownProxies.Add(IPAddress.Parse(ip));
            foreach (string cidr in section.GetSection("KnownNetworks").Get<string[]>() ?? [])
                o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
            // Platforms like Render front the app with proxies whose addresses aren't published. Trusting
            // them is safe with ForwardLimit = 1: only the entry the platform's proxy appends is used, so a
            // client-supplied X-Forwarded-For can't spoof its IP. Never enable this when the app is reachable
            // without going through that proxy.
            if (section.GetValue<bool>("TrustAllProxies"))
            {
                o.KnownProxies.Clear();
                o.KnownIPNetworks.Clear();
#pragma warning disable ASPDEPR005 // the legacy list still carries the loopback default
                o.KnownNetworks.Clear();
#pragma warning restore ASPDEPR005
            }
        });
        return services;
    }

    public static void UseReverseProxySupport(this WebApplication app)
    {
        if (app.Configuration.GetValue<bool>("ReverseProxy:Enabled")) app.UseForwardedHeaders();
    }

    /// <summary>
    /// Locks down the in-browser runner's files (/wasm-runner/). A dedicated worker takes its CSP from its own
    /// script's response, so the worker may load scripts and fetch files only from /wasm-runner/ and connect
    /// nowhere else (no API calls, no other origin). The worker also removes network and storage APIs itself
    /// once the runtime has loaded.
    /// </summary>
    public static void UseBrowserRunnerHeaders(this WebApplication app)
    {
        app.Use((ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/wasm-runner"))
            {
                string runner = $"{ctx.Request.Scheme}://{ctx.Request.Host}/wasm-runner/";
                var headers = ctx.Response.Headers;
                // worker-src must allow the worker's own script: Firefox checks it against the worker itself
                // ("'none'" stops it loading). Nested workers are still limited to these files, and the worker
                // removes its Worker constructor after start-up anyway.
                headers.ContentSecurityPolicy =
                    $"default-src 'none'; script-src {runner} 'wasm-unsafe-eval'; connect-src {runner}; worker-src {runner}; base-uri 'none'; form-action 'none'";
                headers.XContentTypeOptions = "nosniff";
                headers["Cross-Origin-Resource-Policy"] = "same-origin";
            }
            return next();
        });
    }

    public static void MapAccountEndpoints(this WebApplication app)
    {
        // Sign-out is a POST with an antiforgery token (validated by UseAntiforgery for form posts),
        // so another site can't sign people out with a link or image.
        app.MapPost("/account/logout", async (HttpContext ctx, AccountService accounts) =>
        {
            // UseAntiforgery records the result; a missing or forged token is a plain 400, and the form
            // is only read after the token checked out.
            if (ctx.Features.Get<Microsoft.AspNetCore.Antiforgery.IAntiforgeryValidationFeature>() is not { IsValid: true })
                return Results.Text("This sign-out request is missing its security token. Go back, reload the page and try again.", statusCode: StatusCodes.Status400BadRequest);
            var form = await ctx.Request.ReadFormAsync();
            if (ctx.User.Identity?.IsAuthenticated == true) await accounts.SignOutAsync();
            return Results.LocalRedirect(ReturnUrl.Safe(form["returnUrl"]));
        }).WithMetadata(new Microsoft.AspNetCore.Antiforgery.RequireAntiforgeryTokenAttribute());
    }
}

internal sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            return await db.Database.CanConnectAsync(timeout.Token)
                ? HealthCheckResult.Healthy("Account database reachable.")
                : HealthCheckResult.Unhealthy("Account database unreachable.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Account database unreachable.");
        }
    }
}

internal sealed class SandboxHealthCheck(ICodeExecutor executor) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var status = await executor.GetStatusAsync(ct);
        // Without the sandbox, visualizers and the library still work; only "Run" is off: degraded, not down.
        return status.Available ? HealthCheckResult.Healthy("Code sandbox ready.") : HealthCheckResult.Degraded("Code execution unavailable.");
    }
}

internal static class Startup
{
    public static async Task TryMigrateAsync(WebApplication app)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
            app.Logger.LogInformation("Account database is up to date.");
        }
        catch (Exception ex)
        {
            // Development convenience only; a database outage must not stop the rest of the site.
            app.Logger.LogWarning("Could not apply account migrations at startup ({Error}). Accounts will report as unavailable until the database is reachable.", ex.GetType().Name);
        }
    }

    public static async Task<int> MigrateAndExitAsync(WebApplication app, AccountsStatus accounts)
    {
        if (!accounts.Enabled)
        {
            app.Logger.LogError("--migrate needs ConnectionStrings__AlgoSharp to be set.");
            return 2;
        }
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        app.Logger.LogInformation("Applying {Count} migration(s): {Names}", pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync();
        app.Logger.LogInformation("Account database is up to date.");
        return 0;
    }
}
