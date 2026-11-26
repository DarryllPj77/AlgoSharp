using AlgoSharp.Executor;
using AlgoSharp.Playground.Execution;

// AlgoSharp.Executor: runs user code for the public website in disposable, locked-down containers.
// It holds no database credentials and no user data; it only sees compiled solutions and test inputs.
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k =>
{
    k.AddServerHeader = false;
    k.Limits.MaxRequestBodySize = 1024 * 1024;
    k.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
});

var options = builder.Configuration.GetSection("Executor").Get<ExecutorOptions>() ?? new ExecutorOptions();
var sandbox = builder.Configuration.GetSection("Sandbox").Get<SandboxOptions>() ?? new SandboxOptions();
ExecutorProtocol.ParseKey(options.Key); // fail fast: no key, no service

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(sandbox);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISandbox>(new DockerSandbox(new DockerExecutor(sandbox)));
builder.Services.AddSingleton<IsolationState>();
builder.Services.AddSingleton(new Quotas(options.Limits, TimeProvider.System));
builder.Services.AddSingleton<NonceCache>();
builder.Services.AddSingleton<ExecutorHandler>();
builder.Services.AddHostedService<SelfTestWorker>();

builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
{
    // Behind Caddy the client address arrives in X-Forwarded-For; only Caddy's network is trusted for it,
    // so callers can't fake an allowed address.
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor;
    o.ForwardLimit = 1;
    o.KnownProxies.Clear();
    o.KnownIPNetworks.Clear();
#pragma warning disable ASPDEPR005
    o.KnownNetworks.Clear();
#pragma warning restore ASPDEPR005
    foreach (string cidr in options.TrustedProxyNetworks) o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
});

var app = builder.Build();
if (options.TrustedProxyNetworks.Length > 0) app.UseForwardedHeaders();

// Liveness only: says nothing about configuration or isolation (that's behind authentication).
app.MapGet("/healthz", () => Results.Text("ok"));
app.MapPost("/v1/status", (HttpContext ctx, ExecutorHandler h) => h.HandleStatusAsync(ctx));
app.MapPost("/v1/run", (HttpContext ctx, ExecutorHandler h) => h.HandleRunAsync(ctx));
app.MapFallback((HttpContext ctx) => { ctx.Response.StatusCode = 404; return Task.CompletedTask; });

app.Run();
