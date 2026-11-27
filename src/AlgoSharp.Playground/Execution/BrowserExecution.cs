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
                return Fail($"The in-browser runner stopped unexpectedly: {Clip(envelope.Error)}");
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
        if (response is null) return Fail("The browser runner returned no result.");
        if (response.Cases.Count > request.Cases.Count) return Fail("The browser runner returned more results than test cases.");

        // Keep the trace within the limits the request asked for (Tr stops at MaxEvents; allow the final events).
        var cases = response.Cases.Select(c => c with
        {
            Output = c.Output.Length > request.MaxOutputChars + 64 ? c.Output[..request.MaxOutputChars] : c.Output,
            Events = c.Events.Count > request.MaxEvents + 8 ? [.. c.Events.Take(request.MaxEvents + 8)] : c.Events,
        }).ToList();
        return new SandboxResult(true, null, response with { Cases = cases });
    }

    private static SandboxResult Fail(string message) => new(false, message, null);

    private static string Clip(string? s) => string.IsNullOrWhiteSpace(s) ? "unknown error." : s.Length > 200 ? s[..200] + "…" : s;
}
