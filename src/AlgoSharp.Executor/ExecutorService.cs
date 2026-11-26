using System.Net;
using System.Text.Json;
using AlgoSharp.Playground.Execution;
using AlgoSharp.Tracing;

namespace AlgoSharp.Executor;

/// <summary>The container sandbox, behind an interface so the service logic can be tested without Docker.</summary>
public interface ISandbox
{
    Task<IsolationReport> SelfTestAsync(CancellationToken ct);
    Task<SandboxResult> RunAsync(RunRequest request, CancellationToken ct);
    Task<int> SweepAsync(TimeSpan age, CancellationToken ct);
}

public sealed class DockerSandbox(DockerExecutor docker) : ISandbox
{
    public Task<IsolationReport> SelfTestAsync(CancellationToken ct) => docker.SelfTestAsync(ct);
    public Task<SandboxResult> RunAsync(RunRequest request, CancellationToken ct) => docker.RunAsync(request, ct);
    public Task<int> SweepAsync(TimeSpan age, CancellationToken ct) => docker.SweepAsync(age, ct);
}

/// <summary>The latest isolation self-test. No code runs until one has passed (fail closed).</summary>
public sealed class IsolationState
{
    private volatile IsolationReport? latest;
    public IsolationReport? Latest => latest;
    public void Set(IsolationReport report) => latest = report;
    public bool Healthy(TimeProvider clock) => latest is { Passed: true } r && clock.GetUtcNow() - r.CheckedAt < TimeSpan.FromMinutes(30);
}

/// <summary>Runs the isolation self-test at startup and periodically, and sweeps leftover containers.</summary>
public sealed class SelfTestWorker(ISandbox sandbox, IsolationState state, ExecutorOptions options, SandboxOptions sandboxOptions, ILogger<SelfTestWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var report = await sandbox.SelfTestAsync(ct);
                state.Set(report);
                if (report.Passed) log.LogInformation("Isolation self-test passed (runtime {Runtime})", report.Runtime);
                else log.LogError("Isolation self-test FAILED: {Failure}. Code execution is refused until it passes.", report.Failure);
                int removed = await sandbox.SweepAsync(TimeSpan.FromSeconds(sandboxOptions.TimeoutSeconds + 60), ct);
                if (removed > 0) log.LogWarning("Removed {Count} leftover sandbox container(s)", removed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                log.LogError("Self-test error: {Error}", ex.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.SelfTestMinutes)), ct);
        }
    }
}

/// <summary>
/// Authenticated endpoints. Every /v1 request must carry a fresh timestamp, an unused nonce and a valid
/// HMAC signature (and come from an allowed network, if configured); otherwise it gets a bare 401 and no
/// work is done. Answers are signed so the web app can tell them from anyone else's.
/// </summary>
public sealed class ExecutorHandler(
    ISandbox sandbox, IsolationState isolation, Quotas quotas, NonceCache nonces,
    ExecutorOptions options, SandboxOptions sandboxOptions, TimeProvider clock, ILogger<ExecutorHandler> audit)
{
    private readonly byte[] key = ExecutorProtocol.ParseKey(options.Key);
    private readonly IPNetwork[] allowed = options.AllowedNetworks.Select(IPNetwork.Parse).ToArray();

    private sealed record Verified(string Nonce, string Caller, byte[] Body);

    public async Task HandleStatusAsync(HttpContext ctx)
    {
        var request = await VerifyAsync(ctx);
        if (request is null) return;
        var report = isolation.Latest;
        bool ok = isolation.Healthy(clock);
        var status = new ExecutorStatus(ok, ok ? "Sandbox ready." : "Sandbox unavailable.", report);
        await WriteSignedAsync(ctx, request.Nonce, HttpStatusCode.OK, status);
    }

    public async Task HandleRunAsync(HttpContext ctx)
    {
        var request = await VerifyAsync(ctx);
        if (request is null) return;
        string who = request.Caller[..8];
        var started = clock.GetUtcNow();

        if (!isolation.Healthy(clock))
        {
            Audit("refused", who, request.Body.Length, 0, started, "isolation-not-verified");
            await WriteMessageAsync(ctx, request.Nonce, HttpStatusCode.ServiceUnavailable, RemoteExecutor.Unavailable);
            return;
        }

        RunRequest? run;
        try { run = JsonSerializer.Deserialize<RunRequest>(request.Body, TraceJson.Options); }
        catch (JsonException) { run = null; }
        if (run is null || run.Cases is null || string.IsNullOrEmpty(run.AssemblyBase64) || string.IsNullOrEmpty(run.TypeName) || string.IsNullOrEmpty(run.MethodName))
        {
            Audit("rejected", who, request.Body.Length, 0, started, "malformed");
            await WriteMessageAsync(ctx, request.Nonce, HttpStatusCode.UnprocessableEntity, "The run request was malformed.");
            return;
        }
        if (run.Cases.Count is 0 || run.Cases.Count > options.MaxCases)
        {
            Audit("rejected", who, request.Body.Length, run.Cases.Count, started, "case-count");
            await WriteMessageAsync(ctx, request.Nonce, HttpStatusCode.UnprocessableEntity, $"Runs can have 1 to {options.MaxCases} test cases.");
            return;
        }
        // The server's limits win over whatever the request asks for.
        run = run with
        {
            MaxEvents = Math.Clamp(run.MaxEvents, 1, sandboxOptions.MaxEvents),
            MaxOutputChars = Math.Clamp(run.MaxOutputChars, 0, sandboxOptions.MaxOutputChars),
        };

        using var lease = quotas.TryAcquire(request.Caller, out string? reason);
        if (lease is null)
        {
            Audit("throttled", who, request.Body.Length, run.Cases.Count, started, "quota");
            await WriteMessageAsync(ctx, request.Nonce, HttpStatusCode.TooManyRequests, reason!);
            return;
        }

        SandboxResult result;
        try
        {
            result = await sandbox.RunAsync(run, ctx.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Audit("failed", who, request.Body.Length, run.Cases.Count, started, ex.GetType().Name);
            await WriteMessageAsync(ctx, request.Nonce, HttpStatusCode.ServiceUnavailable, RemoteExecutor.Unavailable);
            return;
        }
        if (!result.Ok || result.Response is null)
        {
            // Timeouts, memory limits, crashes of the user's program: safe, user-facing messages from the sandbox.
            Audit("ended", who, request.Body.Length, run.Cases.Count, started, "sandbox-limit-or-error");
            await WriteMessageAsync(ctx, request.Nonce, HttpStatusCode.UnprocessableEntity, result.Error ?? "Execution failed.");
            return;
        }
        Audit("ok", who, request.Body.Length, run.Cases.Count, started, result.Response.Ok ? "completed" : "runner-error");
        await WriteSignedAsync(ctx, request.Nonce, HttpStatusCode.OK, result.Response);
    }

    private async Task<Verified?> VerifyAsync(HttpContext ctx)
    {
        var req = ctx.Request;
        var ip = ctx.Connection.RemoteIpAddress;
        if (allowed.Length > 0 && (ip is null || !allowed.Any(n => n.Contains(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip))))
            return await Reject(ctx, "network");

        string? ts = req.Headers[ExecutorProtocol.TimestampHeader];
        string? nonce = req.Headers[ExecutorProtocol.NonceHeader];
        string? caller = req.Headers[ExecutorProtocol.CallerHeader];
        string? signature = req.Headers[ExecutorProtocol.SignatureHeader];
        if (!ExecutorProtocol.TimestampFresh(ts, clock.GetUtcNow())) return await Reject(ctx, "timestamp");
        if (nonce is not { Length: 32 } || !nonce.All(Uri.IsHexDigit)) return await Reject(ctx, "nonce");
        if (caller is null || (caller != "status" && (caller.Length != 32 || !caller.All(Uri.IsHexDigit)))) return await Reject(ctx, "caller");
        if (req.ContentLength > options.MaxRequestBytes) return await Reject(ctx, "size", HttpStatusCode.RequestEntityTooLarge);

        byte[] body;
        using (var buffer = new MemoryStream())
        {
            var chunk = new byte[16384];
            int read;
            while ((read = await req.Body.ReadAsync(chunk, ctx.RequestAborted)) > 0)
            {
                if (buffer.Length + read > options.MaxRequestBytes) return await Reject(ctx, "size", HttpStatusCode.RequestEntityTooLarge);
                buffer.Write(chunk, 0, read);
            }
            body = buffer.ToArray();
        }

        string expected = ExecutorProtocol.SignRequest(key, req.Method, req.Path.Value ?? "", ts!, nonce, caller, body);
        if (!ExecutorProtocol.Matches(expected, signature)) return await Reject(ctx, "signature");
        // Only after the signature checks out, so strangers can't fill the cache.
        if (!nonces.TryUse(nonce)) return await Reject(ctx, "replay");
        if (req.Path == "/v1/run" && caller == "status") return await Reject(ctx, "caller");
        return new Verified(nonce, caller, body);
    }

    private async Task<Verified?> Reject(HttpContext ctx, string why, HttpStatusCode code = HttpStatusCode.Unauthorized)
    {
        audit.LogWarning("audit event=unauthorized reason={Reason} ip={Ip} path={Path}", why, ctx.Connection.RemoteIpAddress, ctx.Request.Path.Value);
        ctx.Response.StatusCode = (int)code;
        await ctx.Response.CompleteAsync();
        return null;
    }

    private void Audit(string outcome, string caller, int bytes, int cases, DateTimeOffset started, string detail) =>
        // Never the submitted code or results: who (opaque prefix), how much, how long, what happened.
        audit.LogInformation("audit event=run outcome={Outcome} caller={Caller} bytes={Bytes} cases={Cases} ms={Ms} detail={Detail}",
            outcome, caller, bytes, cases, (int)(clock.GetUtcNow() - started).TotalMilliseconds, detail);

    private Task WriteMessageAsync(HttpContext ctx, string nonce, HttpStatusCode code, string message) =>
        WriteSignedAsync(ctx, nonce, code, new { message });

    private async Task WriteSignedAsync<T>(HttpContext ctx, string nonce, HttpStatusCode code, T value)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value, TraceJson.Options);
        ctx.Response.StatusCode = (int)code;
        ctx.Response.ContentType = "application/json";
        ctx.Response.Headers.CacheControl = "no-store";
        ctx.Response.Headers[ExecutorProtocol.SignatureHeader] = ExecutorProtocol.SignResponse(key, nonce, (int)code, body);
        await ctx.Response.Body.WriteAsync(body, ctx.RequestAborted);
    }
}
