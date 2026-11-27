using System.Text.Json;
using AlgoSharp.Tracing;

namespace AlgoSharp.Playground.Execution;

/// <summary>
/// Sandbox:Mode=Browser. User code runs on the visitor's device: the server compiles and instruments it with
/// Roslyn (never executing it), the page hands the assembly to a .NET WebAssembly runtime in a dedicated Web
/// Worker, and the worker returns the runner's own trace. This server-side executor never runs anything:
/// its RunAsync always refuses, and the page uses <see cref="BrowserExecution"/> instead.
/// </summary>
public sealed class BrowserModeExecutor(string message) : ICodeExecutor
{
    public Task<SandboxStatus> GetStatusAsync(CancellationToken ct = default) => Task.FromResult(new SandboxStatus(true, message));

    public Task<SandboxResult> RunAsync(RunRequest request, CancellationToken ct = default) =>
        Task.FromResult(new SandboxResult(false, "Browser execution runs on your device; the server never executes code.", null));
}

/// <summary>What the page reports back from the worker (see wwwroot/js/wasm-exec.js).</summary>
public sealed record BrowserRunEnvelope(string Status, string? Response, string? Error, int? Ms);

public static class BrowserExecution
{
    /// <summary>
    /// Validates the page's answer and turns it into a sandbox result. The browser is the user's own device, so its
    /// answer is treated as untrusted input: size-capped, depth-capped, and clipped to what was asked for.
    /// </summary>
    public static SandboxResult Interpret(string envelopeJson, RunRequest request, int timeoutSeconds)
    {
        BrowserRunEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<BrowserRunEnvelope>(envelopeJson, TraceJson.Options);
        }
        catch (JsonException)
        {
            return Fail("The browser runner returned an unreadable answer.");
        }
        switch (envelope?.Status)
        {
            case "ok":
                break;
            case "timeout":
                return Fail($"Time limit exceeded: your code ran for more than {timeoutSeconds} s and was stopped.");
            case "unsupported":
                return Fail("This browser can't run code: it needs Web Workers and WebAssembly support.");
            case "startup":
                return Fail($"The in-browser runner couldn't start: {Clip(envelope.Error)}");
            case "crashed":
                return Fail("The in-browser runner stopped unexpectedly. This usually means a stack overflow from recursion " +
                            "that never ends (for example a lambda calling itself), which the step limit can't catch.");
            default:
                return Fail("The browser runner returned an unknown status.");
        }

        RunResponse? response;
        try
        {
            response = envelope.Response is null ? null : JsonSerializer.Deserialize<RunResponse>(envelope.Response, TraceJson.Options);
        }
        catch (JsonException)
        {
            return Fail("The browser runner returned an unreadable trace.");
        }
        if (response?.Cases is null) return Fail("The browser runner returned no result.");
        if (response.Cases.Count > request.Cases.Count) return Fail("The browser runner returned more results than test cases.");
        // A modified browser can send anything. The real tracer never exceeds these bounds, and the server's
        // frame builder walks node links recursively, so anything outside them is refused rather than drawn.
        if (!response.Cases.All(WithinTracerLimits)) return Fail("The browser runner returned a trace outside AlgoSharp's limits.");

        // Keep the trace within the limits the request asked for (Tr stops at MaxEvents; allow the final events).
        var cases = response.Cases.Select(c => c with
        {
            Output = c.Output.Length > request.MaxOutputChars + 64 ? c.Output[..request.MaxOutputChars] : c.Output,
            Events = c.Events.Count > request.MaxEvents + 8 ? [.. c.Events.Take(request.MaxEvents + 8)] : c.Events,
        }).ToList();
        return new SandboxResult(true, null, response with { Cases = cases });
    }

    // Generous multiples of the tracer's own caps (64 items per value, 40-character strings).
    private const int MaxValueItems = 65, MaxVarsPerEvent = 256, MaxNodesPerEvent = 2048, MaxText = 2048, MaxName = 256;

    private static bool WithinTracerLimits(CaseResult? c) =>
        c is { Events: not null, Output: not null } && c.Output.Length <= 1024 * 1024 &&
        Short(c.Label, MaxName) && Short(c.ErrorType, MaxName) && Short(c.ErrorMessage, 4096) && Short(c.ResultJson, 256 * 1024) &&
        c.Events.All(e => e is { Vars: not null, Accesses: not null } &&
            e.Line is >= 0 and < 100_000 && e.Depth is >= 0 and < 100_000 && Short(e.Method, MaxName) && Short(e.Text, MaxText) &&
            e.Vars.Count <= MaxVarsPerEvent && e.Accesses.Count <= MaxVarsPerEvent &&
            e.Vars.All(v => v?.Value is not null) && e.Vars.Sum(v => v.Value.Nodes?.Count ?? 0) <= MaxNodesPerEvent &&
            e.Vars.All(v => Short(v.Name, MaxName) && ValueOk(v.Value)) &&
            e.Accesses.All(a => a is not null && Short(a.Key, MaxText) && Short(a.IndexName, MaxName) && Short(a.Kind, MaxName)));

    private static bool ValueOk(TraceValue v) =>
        Short(v.Kind, MaxName) && Short(v.Type, MaxName) && Short(v.Text, MaxText) &&
        (v.Items?.Count ?? 0) <= MaxValueItems && (v.Items?.All(i => Short(i, MaxText)) ?? true) &&
        (v.Entries?.Count ?? 0) <= MaxValueItems && (v.Entries?.All(e => e is not null && Short(e.Key, MaxText) && Short(e.Value, MaxText)) ?? true) &&
        (v.Nodes?.Count ?? 0) <= MaxValueItems && (v.Nodes?.All(n => n is not null && Short(n.Value, MaxText)) ?? true);

    private static bool Short(string? s, int max) => s is null || s.Length <= max;

    private static SandboxResult Fail(string message) => new(false, message, null);

    private static string Clip(string? s) => string.IsNullOrWhiteSpace(s) ? "unknown error." : s.Length > 200 ? s[..200] + "…" : s;
}
