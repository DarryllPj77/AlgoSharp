using System.Text.Json;
using AlgoSharp.Playground.Execution;
using AlgoSharp.Tracing;
using Microsoft.JSInterop;

namespace AlgoSharp.Web.Services;

/// <summary>
/// Per-circuit bridge to the visitor's in-browser runner (wwwroot/js/wasm-exec.js). The compiled, instrumented
/// assembly goes to the page; the page's dedicated Web Worker runs it on the visitor's device and streams the
/// runner's response back. Nothing is executed in this process.
/// </summary>
public sealed class BrowserRunner(IJSRuntime js, SandboxOptions options, ILogger<BrowserRunner> log) : IAsyncDisposable
{
    private const int StartupTimeoutMs = 60_000;
    private IJSObjectReference? module;

    private async Task<IJSObjectReference> ModuleAsync() =>
        module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/wasm-exec.js");

    /// <summary>Starts downloading the runtime in the background so the first run is quick.</summary>
    public async Task PrepareAsync()
    {
        try { await (await ModuleAsync()).InvokeVoidAsync("prepare"); }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
    }

    public async Task<SandboxResult> RunAsync(RunRequest request, CancellationToken ct = default)
    {
        string requestJson = JsonSerializer.Serialize(request, TraceJson.Options);
        int runTimeoutMs = options.TimeoutSeconds * 1000;
        try
        {
            var mod = await ModuleAsync();
            // Interop calls have their own timeout; allow for startup plus the run itself.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(StartupTimeoutMs + runTimeoutMs + 10_000));
            var stream = await mod.InvokeAsync<IJSStreamReference>("run", cts.Token, requestJson, runTimeoutMs, StartupTimeoutMs);
            await using var data = await stream.OpenReadStreamAsync(options.MaxResponseBytes, cts.Token);
            using var reader = new StreamReader(data);
            string envelope = await reader.ReadToEndAsync(cts.Token);
            return BrowserExecution.Interpret(envelope, request, options.TimeoutSeconds);
        }
        catch (OperationCanceledException)
        {
            return new SandboxResult(false, "The browser didn't answer in time.", null) { FailureKind = "timeout" };
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or IOException)
        {
            log.LogWarning("Browser run failed: {Type}: {Message}", ex.GetType().Name, ex.Message);
            return new SandboxResult(false, "The in-browser runner failed: the trace was missing or too large.", null) { FailureKind = "invalid-trace" };
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (module is null) return;
        try
        {
            await module.InvokeVoidAsync("dispose");
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
    }
}
