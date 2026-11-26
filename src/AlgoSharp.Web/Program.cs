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
builder.Services.AddSingleton<ICodeExecutor>(_ => sandbox.Mode.Equals("Docker", StringComparison.OrdinalIgnoreCase)
    ? new DockerExecutor(sandbox)
    : new DisabledExecutor("Code execution is turned off in this deployment (Sandbox:Mode is not \"Docker\")."));
builder.Services.AddSingleton<PlaygroundService>();
builder.Services.AddScoped<AlgoSharp.Web.Services.LocalLibrary>();

// Accounts: ASP.NET Core Identity on PostgreSQL (Neon). Optional: without a connection string the
// site runs as before and the account pages say accounts are off.
var accounts = builder.Services.AddAlgoSharpAccounts(builder.Configuration, builder.Environment);
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingAuthStateProvider>();
builder.Services.AddAuthorization();

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

app.UseReverseProxySupport();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (app.Configuration.GetValue("HttpsRedirection", true)) app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapAlgoSharpHealthChecks();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapAccountEndpoints();

app.Run();
return 0;
