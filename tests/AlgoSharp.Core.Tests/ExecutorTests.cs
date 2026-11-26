using System.Net;
using System.Text;
using System.Text.Json;
using AlgoSharp.Executor;
using AlgoSharp.Playground.Execution;
using AlgoSharp.Tracing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AlgoSharp.Core.Tests;

/// <summary>
/// The executor service's real request handling (signatures, replay, quotas, fail-closed isolation),
/// driven through the real RemoteExecutor client. Only the container sandbox is faked.
/// </summary>
public sealed class ExecutorTests
{
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private sealed class FakeSandbox : ISandbox
    {
        public int Runs;
        public TaskCompletionSource? Hold;
        public Task<IsolationReport> SelfTestAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int> SweepAsync(TimeSpan age, CancellationToken ct) => Task.FromResult(0);
        public async Task<SandboxResult> RunAsync(RunRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref Runs);
            if (Hold is not null) await Hold.Task;
            var events = new List<TraceEvent>();
            return new(true, null, new RunResponse(true, null, [new CaseResult("Example 1", true, "[0,1]", null, null, null, false, "", events)]));
        }
    }

    private static IsolationReport Passed(string runtime = "runc (default)") =>
        new(true, runtime, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["uid"] = "65534" }, null);

    /// <summary>Routes HttpClient calls into the handler; optionally lets a test tamper with requests/responses.</summary>
    private sealed class InProcess(ExecutorHandler handler, IPAddress? ip = null) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task>? BeforeSend;
        public Func<byte[], byte[]>? TamperResponse;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (BeforeSend is not null) await BeforeSend(request);
            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = ip ?? IPAddress.Loopback;
            ctx.Request.Method = request.Method.Method;
            ctx.Request.Path = request.RequestUri!.AbsolutePath;
            foreach (var h in request.Headers) ctx.Request.Headers[h.Key] = h.Value.ToArray();
            byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
            ctx.Request.Body = new MemoryStream(body);
            ctx.Request.ContentLength = body.Length;
            var output = new MemoryStream();
            ctx.Response.Body = output;

            await (ctx.Request.Path == "/v1/run" ? handler.HandleRunAsync(ctx) : handler.HandleStatusAsync(ctx));

            byte[] responseBody = output.ToArray();
            if (TamperResponse is not null) responseBody = TamperResponse(responseBody);
            var response = new HttpResponseMessage((HttpStatusCode)ctx.Response.StatusCode) { Content = new ByteArrayContent(responseBody) };
            foreach (var h in ctx.Response.Headers) response.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray());
            return response;
        }
    }

    private sealed record Rig(ExecutorHandler Handler, FakeSandbox Sandbox, IsolationState Isolation, ExecutorOptions Options);

    private static Rig MakeRig(IsolationReport? isolation = null, Action<ExecutorOptions>? configure = null)
    {
        var options = new ExecutorOptions { Key = Key };
        configure?.Invoke(options);
        var sandbox = new FakeSandbox();
        var state = new IsolationState();
        if (isolation is not null) state.Set(isolation);
        var handler = new ExecutorHandler(sandbox, state, new Quotas(options.Limits, TimeProvider.System), new NonceCache(TimeProvider.System),
            options, new SandboxOptions(), TimeProvider.System, NullLogger<ExecutorHandler>.Instance);
        return new Rig(handler, sandbox, state, options);
    }

    private static RemoteExecutor Client(InProcess transport, string? requireRuntime = null, string key = "") =>
        new(new RemoteSandboxOptions { Url = "http://localhost:5320", Key = key.Length > 0 ? key : Key, RequireRuntime = requireRuntime }, new HttpClient(transport));

    private static RunRequest Request(int cases = 1) =>
        new(Convert.ToBase64String([1, 2, 3]), "Solution", "TwoSum",
            Enumerable.Range(0, cases).Select(i => new TestCaseRequest($"Case {i}", [])).ToList(), 2000, 8000);

    [Fact]
    public async Task Signed_requests_run_and_return_the_sandbox_result()
    {
        var rig = MakeRig(Passed());
        var client = Client(new InProcess(rig.Handler));
        Assert.True((await client.GetStatusAsync()).Available);
        var result = await client.RunAsync(Request(), client.CallerFor("account-1"));
        Assert.True(result.Ok, result.Error);
        Assert.Equal("[0,1]", Assert.Single(result.Response!.Cases).ResultJson);
        Assert.Equal(1, rig.Sandbox.Runs);
    }

    [Fact]
    public async Task Unsigned_and_wrongly_signed_requests_are_rejected_without_running_anything()
    {
        var rig = MakeRig(Passed());
        using var raw = new HttpClient(new InProcess(rig.Handler)) { BaseAddress = new Uri("http://localhost:5320") };
        var anonymous = await raw.PostAsync("/v1/run", new StringContent(JsonSerializer.Serialize(Request(), TraceJson.Options)));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.False(anonymous.Headers.Contains(ExecutorProtocol.SignatureHeader));

        // A client holding a different key is treated exactly like an anonymous one.
        string otherKey = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray());
        var impostor = Client(new InProcess(rig.Handler), key: otherKey);
        Assert.False((await impostor.GetStatusAsync()).Available);
        Assert.False((await impostor.RunAsync(Request(), impostor.CallerFor("x"))).Ok);
        Assert.Equal(0, rig.Sandbox.Runs);
    }

    [Fact]
    public async Task Replayed_and_stale_requests_are_rejected()
    {
        var rig = MakeRig(Passed());
        HttpRequestMessage? captured = null;
        byte[]? capturedBody = null;
        var transport = new InProcess(rig.Handler)
        {
            BeforeSend = async r => { captured ??= r; capturedBody ??= await r.Content!.ReadAsByteArrayAsync(); },
        };
        var client = Client(transport);
        Assert.True((await client.RunAsync(Request(), client.CallerFor("a"))).Ok);

        // Send the exact same signed request again.
        using var replay = new HttpRequestMessage(HttpMethod.Post, captured!.RequestUri) { Content = new ByteArrayContent(capturedBody!) };
        foreach (var h in captured.Headers) replay.Headers.TryAddWithoutValidation(h.Key, h.Value);
        using var raw = new HttpClient(new InProcess(rig.Handler));
        Assert.Equal(HttpStatusCode.Unauthorized, (await raw.SendAsync(replay)).StatusCode);

        // A correctly signed request whose timestamp is too old.
        byte[] key = Convert.FromBase64String(Key);
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(Request(), TraceJson.Options);
        string ts = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds().ToString(), nonce = ExecutorProtocol.NewNonce(), caller = new string('a', 32);
        using var stale = new HttpRequestMessage(HttpMethod.Post, "http://localhost:5320/v1/run") { Content = new ByteArrayContent(body) };
        stale.Headers.Add(ExecutorProtocol.TimestampHeader, ts);
        stale.Headers.Add(ExecutorProtocol.NonceHeader, nonce);
        stale.Headers.Add(ExecutorProtocol.CallerHeader, caller);
        stale.Headers.Add(ExecutorProtocol.SignatureHeader, ExecutorProtocol.SignRequest(key, "POST", "/v1/run", ts, nonce, caller, body));
        Assert.Equal(HttpStatusCode.Unauthorized, (await raw.SendAsync(stale)).StatusCode);
        Assert.Equal(1, rig.Sandbox.Runs);
    }

    [Fact]
    public async Task Tampered_responses_are_not_trusted()
    {
        var rig = MakeRig(Passed());
        var transport = new InProcess(rig.Handler) { TamperResponse = b => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(b).Replace("[0,1]", "[9,9]")) };
        var client = Client(transport);
        var result = await client.RunAsync(Request(), client.CallerFor("a"));
        Assert.False(result.Ok);
        Assert.Equal(RemoteExecutor.Unavailable, result.Error);
    }

    [Fact]
    public async Task Execution_fails_closed_until_isolation_is_verified()
    {
        var none = MakeRig(isolation: null);
        var client = Client(new InProcess(none.Handler));
        Assert.False((await client.GetStatusAsync()).Available);
        Assert.False((await client.RunAsync(Request(), client.CallerFor("a"))).Ok);

        var failed = MakeRig(new IsolationReport(false, "runc (default)", DateTimeOffset.UtcNow, new Dictionary<string, string>(), "Isolation check failed: dns"));
        client = Client(new InProcess(failed.Handler));
        Assert.False((await client.GetStatusAsync()).Available);
        Assert.False((await client.RunAsync(Request(), client.CallerFor("a"))).Ok);

        var stale = MakeRig(Passed() with { CheckedAt = DateTimeOffset.UtcNow.AddHours(-2) });
        client = Client(new InProcess(stale.Handler));
        Assert.False((await client.GetStatusAsync()).Available);

        // The web app can insist on a hardened runtime (gVisor).
        var runc = MakeRig(Passed("runc (default)"));
        Assert.False((await Client(new InProcess(runc.Handler), requireRuntime: "runsc").GetStatusAsync()).Available);
        var runsc = MakeRig(Passed("runsc"));
        Assert.True((await Client(new InProcess(runsc.Handler), requireRuntime: "runsc").GetStatusAsync()).Available);

        Assert.Equal(0, none.Sandbox.Runs + failed.Sandbox.Runs);
    }

    [Fact]
    public async Task Per_account_rate_and_concurrency_limits_apply()
    {
        var rig = MakeRig(Passed(), o => { o.PerCallerPer10Minutes = 3; o.PerCallerConcurrent = 1; });
        var client = Client(new InProcess(rig.Handler));
        string alice = client.CallerFor("alice"), bob = client.CallerFor("bob");
        for (int i = 0; i < 3; i++) Assert.True((await client.RunAsync(Request(), alice)).Ok);
        var limited = await client.RunAsync(Request(), alice);
        Assert.False(limited.Ok);
        Assert.Contains("runs per 10 minutes", limited.Error);
        Assert.True((await client.RunAsync(Request(), bob)).Ok); // other accounts aren't affected

        // A second run while the first is still going is refused.
        var busy = MakeRig(Passed());
        busy.Sandbox.Hold = new TaskCompletionSource();
        var c2 = Client(new InProcess(busy.Handler));
        string carol = c2.CallerFor("carol");
        var first = c2.RunAsync(Request(), carol);
        while (busy.Sandbox.Runs == 0) await Task.Delay(10);
        var second = await c2.RunAsync(Request(), carol);
        Assert.Contains("still in progress", second.Error);
        busy.Sandbox.Hold.SetResult();
        Assert.True((await first).Ok);
        Assert.True((await c2.RunAsync(Request(), carol)).Ok); // slot freed after the run
    }

    [Fact]
    public async Task Oversized_and_malformed_submissions_are_rejected()
    {
        var rig = MakeRig(Passed(), o => o.MaxRequestBytes = 4096);
        var client = Client(new InProcess(rig.Handler));
        var big = Request() with { AssemblyBase64 = Convert.ToBase64String(new byte[8192]) };
        Assert.False((await client.RunAsync(big, client.CallerFor("a"))).Ok);
        var tooMany = await client.RunAsync(Request(cases: 11), client.CallerFor("a"));
        Assert.Contains("1 to 10 test cases", tooMany.Error);
        Assert.Equal(0, rig.Sandbox.Runs);
    }

    [Fact]
    public async Task Calls_from_outside_the_allowed_networks_are_rejected()
    {
        var rig = MakeRig(Passed(), o => o.AllowedNetworks = ["10.0.0.0/8"]);
        var outside = Client(new InProcess(rig.Handler, IPAddress.Parse("203.0.113.9")));
        Assert.False((await outside.RunAsync(Request(), outside.CallerFor("a"))).Ok);
        var inside = Client(new InProcess(rig.Handler, IPAddress.Parse("10.1.2.3")));
        Assert.True((await inside.RunAsync(Request(), inside.CallerFor("a"))).Ok);
        Assert.Equal(1, rig.Sandbox.Runs);
    }

    [Fact]
    public void Caller_ids_are_opaque_and_stable()
    {
        byte[] key = Convert.FromBase64String(Key);
        string a = ExecutorProtocol.CallerKey(key, "1b2c-account");
        Assert.Equal(a, ExecutorProtocol.CallerKey(key, "1b2c-account"));
        Assert.NotEqual(a, ExecutorProtocol.CallerKey(key, "other-account"));
        Assert.DoesNotContain("account", a);
        Assert.Equal(32, a.Length);
    }

    [Fact]
    public void Remote_client_requires_https_and_a_strong_key()
    {
        Assert.Throws<InvalidOperationException>(() => new RemoteExecutor(new RemoteSandboxOptions { Url = "http://runner.example.com", Key = Key }, new HttpClient()));
        Assert.Throws<InvalidOperationException>(() => new RemoteExecutor(new RemoteSandboxOptions { Url = "https://runner.example.com", Key = Convert.ToBase64String(new byte[16]) }, new HttpClient()));
        Assert.Throws<InvalidOperationException>(() => new RemoteExecutor(new RemoteSandboxOptions { Url = "https://runner.example.com", Key = null }, new HttpClient()));
    }

    [Fact]
    public void Every_sandbox_container_gets_the_hardening_flags()
    {
        var args = DockerExecutor.HardeningArgs(new SandboxOptions(), "algosharp-run-x");
        string joined = string.Join(' ', args);
        foreach (string flag in new[] { "--rm", "--network none", "--read-only", "--cap-drop ALL", "--security-opt no-new-privileges", "--user 65534:65534", "--pids-limit 64", "--memory 256m", "--memory-swap 256m" })
            Assert.Contains(flag, joined);
        Assert.DoesNotContain("-v", args);
        Assert.DoesNotContain("--volume", args);
        Assert.DoesNotContain("-e", args);
        Assert.DoesNotContain("--privileged", args);
        Assert.Contains("--runtime runsc", string.Join(' ', DockerExecutor.HardeningArgs(new SandboxOptions { Runtime = "runsc" }, "n")));
    }
}
