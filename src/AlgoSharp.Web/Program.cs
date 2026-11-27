using AlgoSharp.Accounts;
using AlgoSharp.Playground;
using AlgoSharp.Playground.Execution;
using AlgoSharp.Web.Components;
using AlgoSharp.Web.Hosting;
using Microsoft.AspNetCore.Components.Authorization;

// Container health probe (the runtime image has no curl): `dotnet AlgoSharp.Web.dll --healthcheck`.
if (args.Contains("--healthcheck"))
{
    string port = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080").Split(';', ',')[0];
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    try { return (await http.GetAsync($"http://127.0.0.1:{port}/healthz")).IsSuccessStatusCode ? 0 : 1; }
    catch { return 1; }
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// User code never runs in this process. It is compiled here (no execution) and executed only
// inside an isolated Docker container; without Docker, execution is disabled.
var sandbox = builder.Configuration.GetSection("Sandbox").Get<SandboxOptions>() ?? new SandboxOptions();
builder.Services.AddSingleton(sandbox);
// Docker: local containers (development). Remote: the separate AlgoSharp.Executor service, used only while
// it proves (signed status + passing isolation self-test) that it's available. Anything else: disabled.
builder.Services.AddSingleton<ICodeExecutor>(sp => sandbox.Mode.ToLowerInvariant() switch
{
    "docker" => new DockerExecutor(sandbox),
    // Browser: runs on the visitor's device in a .NET WebAssembly Web Worker; this server only compiles.
    "browser" => new BrowserModeExecutor(sandbox.BrowserMessage),
    "remote" => new RemoteExecutor(sandbox.Remote,
        new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), AllowAutoRedirect = false }),
        message => sp.GetRequiredService<ILogger<RemoteExecutor>>().LogWarning("{Message}", message)),
    _ => new DisabledExecutor(sandbox.DisabledMessage ?? "Code execution is turned off in this deployment (Sandbox:Mode is not \"Docker\")."),
});
// Per-account limits applied before any run leaves this server (the executor enforces its own too).
builder.Services.AddSingleton(new Quotas(
    builder.Configuration.GetSection("Execution:Limits").Get<QuotaLimits>() ?? new QuotaLimits(PerCallerPer10Minutes: 30, PerCallerPerDay: 300, PerCallerConcurrent: 1, GlobalPerMinute: 120),
    TimeProvider.System));
builder.Services.AddSingleton<PlaygroundService>();
builder.Services.AddScoped<AlgoSharp.Web.Services.LocalLibrary>();
builder.Services.AddScoped<AlgoSharp.Web.Services.BrowserRunner>();
builder.Services.AddScoped<AlgoSharp.Web.Services.SoundCues>();

// Accounts: ASP.NET Core Identity on PostgreSQL (Neon). Optional: without a connection string the
// site runs as before and the account pages say accounts are off.
var accounts = builder.Services.AddAlgoSharpAccounts(builder.Configuration, builder.Environment);
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingAuthStateProvider>();
builder.Services.AddAuthorization();

// Request-size limits: account forms are tiny, and everything interactive goes over the circuit.
builder.WebHost.ConfigureKestrel(k =>
{
    k.Limits.MaxRequestBodySize = 2 * 1024 * 1024;
    k.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.ValueLengthLimit = 16 * 1024;
    o.ValueCountLimit = 64;
    o.MultipartBodyLengthLimit = 2 * 1024 * 1024;
});
builder.Services.AddAntiforgery(o =>
{
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = true;
});

builder.Services.AddAccountRateLimiting();
builder.Services.AddAlgoSharpHealthChecks(accounts);
builder.Services.AddReverseProxySupport(builder.Configuration);

var app = builder.Build();

// `dotnet AlgoSharp.Web.dll --migrate` applies pending migrations and exits: the controlled way to
// change a production schema (run it once per release, before starting the new version).
if (args.Contains("--migrate"))
{
    return await Startup.MigrateAndExitAsync(app, accounts);
}
if (accounts.Enabled && app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await Startup.TryMigrateAsync(app);
}
if (!accounts.Enabled) app.Logger.LogWarning("Accounts are disabled: {Reason}", accounts.Reason);
if (!app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(app.Configuration["DataProtection:CertificateBase64"]))
    app.Logger.LogWarning("Data Protection keys are stored unencrypted: set DataProtection__CertificateBase64 and DataProtection__CertificatePassword.");

app.UseReverseProxySupport();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// Health probes come straight from the platform over plain HTTP, so they're never redirected.
if (app.Configuration.GetValue("HttpsRedirection", true))
    app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/healthz"), b => b.UseHttpsRedirection());

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapAlgoSharpHealthChecks();
app.UseBrowserRunnerHeaders();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapAccountEndpoints();

app.Run();
return 0;
