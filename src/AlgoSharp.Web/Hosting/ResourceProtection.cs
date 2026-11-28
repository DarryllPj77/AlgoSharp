using System.Diagnostics;
using AlgoSharp.Playground;
using AlgoSharp.Playground.Compilation;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace AlgoSharp.Web.Hosting;

public sealed class ResourceOptions
{
    /// <summary>Interactive sessions (Blazor circuits) at once. New ones get 503 + Retry-After beyond this.</summary>
    public int MaxCircuits { get; set; } = 60;

    /// <summary>How often the resource summary is logged.</summary>
    public int MonitorSeconds { get; set; } = 60;
}

/// <summary>Counts live interactive sessions (one <see cref="CircuitHandler"/> per circuit feeds one shared counter).</summary>
public sealed class CircuitCounter
{
    private int active, peak;
    public int Active => Volatile.Read(ref active);
    public int Peak => Volatile.Read(ref peak);

    internal void Opened()
    {
        int now = Interlocked.Increment(ref active);
        int p;
        while (now > (p = Volatile.Read(ref peak)) && Interlocked.CompareExchange(ref peak, now, p) != p) { }
    }

    internal void Closed() => Interlocked.Decrement(ref active);
}

internal sealed class CountingCircuitHandler(CircuitCounter counter) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken ct) { counter.Opened(); return Task.CompletedTask; }
    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken ct) { counter.Closed(); return Task.CompletedTask; }
}

/// <summary>Logs a one-line resource summary periodically (counts and sizes only: no code, users or traces).</summary>
internal sealed class ResourceMonitor(CompileGate gate, CircuitCounter circuits, ResourceOptions options, ILogger<ResourceMonitor> log) : BackgroundService
{
    private long peakWorkingSet;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Warm Roslyn once at startup (parse, bind, instrument, emit): on a 0.1-CPU instance the first cold
        // compile takes several seconds, which would otherwise land on the first user to press Run.
        try
        {
            var started = Stopwatch.GetTimestamp();
            if (PlaygroundCatalog.Find("two-sum") is { } problem)
                await Task.Run(() => CodeCompiler.Compile(problem.StarterCode, problem.Entry, ct: stoppingToken), stoppingToken);
            log.LogInformation("Roslyn warm-up done in {Ms} ms", (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.MonitorSeconds)));
        CompileGate.Stats? previous = null;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var s = gate.Snapshot();
            long ws = Environment.WorkingSet;
            if (ws > peakWorkingSet) peakWorkingSet = ws;
            var gc = GC.GetGCMemoryInfo();
            bool rejectedSince = previous is not null && (s.Busy > previous.Busy || s.TimedOut > previous.TimedOut);
            log.Log(rejectedSince ? LogLevel.Warning : LogLevel.Information,
                "resources compile_active={Active} compile_queued={Queued} compile_ok={Ok} compile_busy={Busy} compile_cooldown={Cooldown} " +
                "compile_timeout={TimedOut} compile_superseded={Superseded} compile_avg_ms={AvgMs} compile_max_ms={MaxMs} " +
                "circuits={Circuits} circuits_peak={CircuitsPeak} working_set_mb={WorkingSetMb} peak_working_set_mb={PeakMb} gc_heap_mb={HeapMb}",
                s.Active, s.Queued, s.Ok, s.Busy, s.Cooldown, s.TimedOut, s.Superseded, s.AvgMs, s.MaxMs,
                circuits.Active, circuits.Peak, ws >> 20, peakWorkingSet >> 20, gc.HeapSizeBytes >> 20);
            previous = s;
        }
    }
}

public static class ResourceProtection
{
    public static IServiceCollection AddResourceProtection(this IServiceCollection services, IConfiguration config)
    {
        var limits = config.GetSection("Compilation").Get<CompileLimits>() ?? new CompileLimits();
        var options = config.GetSection("Resources").Get<ResourceOptions>() ?? new ResourceOptions();
        services.AddSingleton(limits);
        services.AddSingleton(options);
        services.AddSingleton(sp => new CompileGate(limits, TimeProvider.System));
        services.AddSingleton<CircuitCounter>();
        services.AddScoped<CircuitHandler, CountingCircuitHandler>();
        services.AddHostedService<ResourceMonitor>();
        services.Configure<CircuitOptions>(o =>
        {
            // Disconnected sessions keep their whole UI state in memory: hold fewer, for less time (defaults: 100, 3 min).
            o.DisconnectedCircuitMaxRetained = 30;
            o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(2);
        });
        return services;
    }

    /// <summary>
    /// When the server already holds <see cref="ResourceOptions.MaxCircuits"/> interactive sessions, new ones are
    /// refused with 503 + Retry-After instead of pushing the process out of memory. Existing users are unaffected;
    /// pages still render, only their interactivity waits.
    /// </summary>
    public static void UseCircuitAdmission(this WebApplication app)
    {
        var circuits = app.Services.GetRequiredService<CircuitCounter>();
        var options = app.Services.GetRequiredService<ResourceOptions>();
        app.Use((ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/_blazor/negotiate") && circuits.Active >= options.MaxCircuits)
            {
                ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                ctx.Response.Headers.RetryAfter = "30";
                return ctx.Response.WriteAsync("AlgoSharp is very busy right now. Please try again in a minute.");
            }
            return next();
        });
    }

    /// <summary>
    /// Load-test endpoints (Diagnostics:LoadTestEndpoints=true only; never enabled in production). They drive the same
    /// compile gate and Roslyn pipeline as Solve It, without execution, so the load test measures the real bottleneck.
    /// Responses: 200 compiled, 429 cooldown, 503 busy/timeout (with Retry-After).
    /// </summary>
    public static void MapLoadTestEndpoints(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Diagnostics:LoadTestEndpoints")) return;
        app.Logger.LogWarning("Diagnostics:LoadTestEndpoints is on: /diag/* endpoints are exposed. Never enable this in production.");

        app.MapPost("/diag/compile", async (LoadTestRequest body, PlaygroundService service, CompileGate gate, HttpContext ctx) =>
        {
            if (body.Code is not { Length: > 0 and <= CodeCompiler.MaxSourceLength } || PlaygroundCatalog.Find(body.Problem ?? "") is not { } problem)
                return Results.BadRequest(new { status = "Invalid", error = $"Unknown problem, or code empty or over {CodeCompiler.MaxSourceLength:N0} characters." });
            string user = "load:" + (body.User ?? "anon");
            long started = Stopwatch.GetTimestamp();
            GateStatus status;
            TimeSpan retryAfter = default;
            int errors = 0;
            if (body.Kind == "run")
            {
                // The server's share of a Run: full compile + instrumentation through the gate; nothing is executed.
                var r = await gate.RunAsync(user, CompileKind.Run, t => CodeCompiler.Compile(body.Code, problem.Entry, ct: t), ctx.RequestAborted);
                (status, retryAfter) = (r.Status, r.RetryAfter);
                errors = r.Value?.Errors.Count() ?? 0;
            }
            else
            {
                var r = await service.CheckAsync(problem, body.Code, user, ctx.RequestAborted);
                (status, retryAfter) = (r.Status, r.RetryAfter);
                errors = r.Value?.Errors.Count() ?? 0;
            }
            double ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var payload = new { status = status.ToString(), ms = Math.Round(ms, 1), errors };
            if (status is not (GateStatus.Ok or GateStatus.Superseded))
                ctx.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
            return status switch
            {
                GateStatus.Ok or GateStatus.Superseded => Results.Ok(payload),
                GateStatus.Cooldown => Results.Json(payload, statusCode: StatusCodes.Status429TooManyRequests),
                _ => Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable),
            };
        }).DisableAntiforgery();

        app.MapGet("/diag/stats", (CompileGate gate, CircuitCounter circuits) =>
        {
            var gc = GC.GetGCMemoryInfo();
            return Results.Ok(new { gate = gate.Snapshot(), circuits = circuits.Active, workingSetMb = Environment.WorkingSet >> 20, gcHeapMb = gc.HeapSizeBytes >> 20 });
        });
    }

    public sealed record LoadTestRequest(string? Problem, string? Code, string? User, string? Kind);
}
