using System.Net;
using System.Text.Json;
using AlgoSharp.Tracing;

namespace AlgoSharp.Playground.Execution;

/// <summary>What the executor service reports on /v1/status.</summary>
public sealed record ExecutorStatus(bool Available, string Message, IsolationReport? Isolation);

/// <summary>
/// Runs code on the separate AlgoSharp.Executor service. Fails closed: Run is only offered while the
/// service answers with a valid signature, says it's available, and its isolation self-test passed
/// (with the required runtime, if one is configured). Any other situation reports "unavailable" with a
/// generic message; details go to the log, not to users.
/// </summary>
public sealed class RemoteExecutor : ICallerAwareExecutor
{
    public const string Unavailable = "Cloud code execution is temporarily unavailable. Your code wasn't run. Please try again in a few minutes.";

    private readonly RemoteSandboxOptions options;
    private readonly HttpClient http;
    private readonly byte[] key;
    private readonly Action<string>? log;
    private SandboxStatus? cached;
    private DateTime cachedAt;

    public RemoteExecutor(RemoteSandboxOptions options, HttpClient http, Action<string>? log = null)
    {
        this.options = options;
        this.http = http;
        this.log = log;
        key = ExecutorProtocol.ParseKey(options.Key);
        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var url)) throw new InvalidOperationException("Sandbox:Remote:Url must be an absolute URL.");
        if (url.Scheme != Uri.UriSchemeHttps && !url.IsLoopback) throw new InvalidOperationException("Sandbox:Remote:Url must use https (plain http is only allowed for localhost).");
        http.BaseAddress = url;
        http.Timeout = TimeSpan.FromSeconds(Math.Max(10, options.TimeoutSeconds));
    }

    /// <summary>The opaque id the executor uses for quotas (never the account id itself).</summary>
    public string CallerFor(string accountId) => ExecutorProtocol.CallerKey(key, accountId);

    public async Task<SandboxStatus> GetStatusAsync(CancellationToken ct = default)
    {
        if (cached is not null && DateTime.UtcNow - cachedAt < TimeSpan.FromSeconds(cached.Available ? 30 : 10)) return cached;
        SandboxStatus status;
        try
        {
            var (code, body) = await SendAsync("/v1/status", "status", [], ct);
            var remote = code == HttpStatusCode.OK ? JsonSerializer.Deserialize<ExecutorStatus>(body, TraceJson.Options) : null;
            status = Evaluate(remote, options.RequireRuntime, out string? why);
            if (why is not null) log?.Invoke($"Remote executor unavailable: {why}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            log?.Invoke($"Remote executor unreachable: {ex.GetType().Name}");
            status = new SandboxStatus(false, Unavailable);
        }
        cached = status;
        cachedAt = DateTime.UtcNow;
        return status;
    }

    /// <summary>The fail-closed policy, separate so it can be tested.</summary>
    public static SandboxStatus Evaluate(ExecutorStatus? remote, string? requireRuntime, out string? why)
    {
        why = remote switch
        {
            null => "no valid status",
            { Available: false } => "service reports unavailable",
            { Isolation: null } => "no isolation self-test",
            { Isolation.Passed: false } => "isolation self-test failed: " + remote.Isolation.Failure,
            _ when DateTimeOffset.UtcNow - remote.Isolation!.CheckedAt > TimeSpan.FromMinutes(30) => "isolation self-test is stale",
            _ when !string.IsNullOrWhiteSpace(requireRuntime) && !string.Equals(remote.Isolation!.Runtime, requireRuntime, StringComparison.Ordinal)
                => $"runtime {remote.Isolation!.Runtime} is not the required {requireRuntime}",
            _ => null,
        };
        return why is null
            ? new SandboxStatus(true, "Cloud sandbox ready.") { Isolation = remote!.Isolation }
            : new SandboxStatus(false, Unavailable) { Isolation = remote?.Isolation };
    }

    public Task<SandboxResult> RunAsync(RunRequest request, CancellationToken ct = default) =>
        Task.FromResult(new SandboxResult(false, "Sign in to run code.", null)); // remote runs are always per account

    public async Task<SandboxResult> RunAsync(RunRequest request, string caller, CancellationToken ct = default)
    {
        var status = await GetStatusAsync(ct);
        if (!status.Available) return new(false, status.Message, null);
        try
        {
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(request, TraceJson.Options);
            var (code, body) = await SendAsync("/v1/run", caller, payload, ct);
            switch (code)
            {
                case HttpStatusCode.OK:
                    var response = JsonSerializer.Deserialize<RunResponse>(body, TraceJson.Options);
                    return response is null ? new(false, Unavailable, null) : new(true, null, response);
                case HttpStatusCode.TooManyRequests:
                case HttpStatusCode.UnprocessableEntity:
                case HttpStatusCode.RequestEntityTooLarge:
                case HttpStatusCode.ServiceUnavailable:
                    // The service's own short, user-safe explanation (quota, busy, too large, timed out...).
                    return new(false, ReadMessage(body) ?? Unavailable, null);
                default:
                    log?.Invoke($"Remote executor returned {(int)code} for a run");
                    InvalidateStatus();
                    return new(false, Unavailable, null);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            if (ct.IsCancellationRequested) throw;
            log?.Invoke($"Remote run failed: {ex.GetType().Name}");
            InvalidateStatus();
            return new(false, Unavailable, null);
        }
    }

    private void InvalidateStatus() => cached = null;

    private async Task<(HttpStatusCode Code, byte[] Body)> SendAsync(string path, string caller, byte[] payload, CancellationToken ct)
    {
        string ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string nonce = ExecutorProtocol.NewNonce();
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(payload) };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add(ExecutorProtocol.TimestampHeader, ts);
        request.Headers.Add(ExecutorProtocol.NonceHeader, nonce);
        request.Headers.Add(ExecutorProtocol.CallerHeader, caller);
        request.Headers.Add(ExecutorProtocol.SignatureHeader, ExecutorProtocol.SignRequest(key, "POST", path, ts, nonce, caller, payload));

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > 16 * 1024 * 1024) throw new InvalidDataException("Executor response too large.");
            buffer.Write(chunk, 0, read);
        }
        byte[] body = buffer.ToArray();
        string? signature = response.Headers.TryGetValues(ExecutorProtocol.SignatureHeader, out var values) ? values.FirstOrDefault() : null;
        // Only signed answers count, including errors: anything else is treated as "service unavailable".
        if (!ExecutorProtocol.Matches(ExecutorProtocol.SignResponse(key, nonce, (int)response.StatusCode, body), signature))
            throw new InvalidDataException("Executor response signature is missing or invalid.");
        return (response.StatusCode, body);
    }

    private static string? ReadMessage(byte[] body)
    {
        try { return JsonDocument.Parse(body).RootElement.GetProperty("message").GetString(); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }
}
