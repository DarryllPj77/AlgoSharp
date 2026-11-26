using AlgoSharp.Playground;
using AlgoSharp.Playground.Execution;
using AlgoSharp.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
