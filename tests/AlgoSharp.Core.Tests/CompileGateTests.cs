using AlgoSharp.Playground;
using AlgoSharp.Playground.Compilation;
using AlgoSharp.Playground.Execution;

namespace AlgoSharp.Core.Tests;

public sealed class CompileGateTests
{
    private static CompileGate Gate(int concurrent = 1, int queued = 1, double checkWait = 0.3, double runWait = 0.3, double timeout = 5, double cooldown = 2) =>
        new(new CompileLimits
        {
            MaxConcurrent = concurrent, MaxQueued = queued, CheckQueueSeconds = checkWait, RunQueueSeconds = runWait,
            TimeoutSeconds = timeout, RunCooldownSeconds = cooldown,
        }, TimeProvider.System);

    [Fact]
    public async Task Caps_concurrency_and_turns_away_when_the_queue_is_full()
    {
        using var gate = Gate(concurrent: 1, queued: 0, checkWait: 0.2);
        var hold = new TaskCompletionSource();
        int running = 0, maxRunning = 0;
        int Work(CancellationToken _)
        {
            int now = Interlocked.Increment(ref running);
            maxRunning = Math.Max(maxRunning, now);
            hold.Task.Wait();
            Interlocked.Decrement(ref running);
            return 1;
        }
        var first = gate.RunAsync("a", CompileKind.Check, Work);
        await Task.Delay(100);
        var second = await gate.RunAsync("b", CompileKind.Check, Work); // no slot, no queue room
        Assert.Equal(GateStatus.Busy, second.Status);
        Assert.Contains("busy", second.Message);
        hold.SetResult();
        Assert.True((await first).Ok);
        Assert.Equal(1, maxRunning);
    }

    [Fact]
    public async Task A_waiting_check_is_dropped_when_the_same_user_types_again()
    {
        using var gate = Gate(concurrent: 1, queued: 5, checkWait: 2);
        var hold = new TaskCompletionSource();
        var blocker = gate.RunAsync("other", CompileKind.Check, _ => { hold.Task.Wait(); return "x"; });
        await Task.Delay(50);
        var older = gate.RunAsync("u", CompileKind.Check, _ => "old code");
        var newer = gate.RunAsync("u", CompileKind.Check, _ => "new code");
        hold.SetResult();
        await blocker;
        Assert.Equal(GateStatus.Superseded, (await older).Status);
        Assert.Equal("new code", (await newer).Value);
    }

    [Fact]
    public async Task Runs_have_a_per_user_cooldown_that_does_not_affect_others()
    {
        using var gate = Gate(cooldown: 30);
        Assert.True((await gate.RunAsync("u", CompileKind.Run, _ => 1)).Ok);
        var again = await gate.RunAsync("u", CompileKind.Run, _ => 1);
        Assert.Equal(GateStatus.Cooldown, again.Status);
        Assert.True(again.RetryAfter > TimeSpan.Zero);
        Assert.True((await gate.RunAsync("someone-else", CompileKind.Run, _ => 1)).Ok);
    }

    [Fact]
    public async Task Long_compilations_time_out_and_cancelled_sessions_release_their_slot()
    {
        using var gate = Gate(concurrent: 1, timeout: 0.2);
        var slow = await gate.RunAsync("u", CompileKind.Check, ct => { Task.Delay(5000, ct).Wait(ct); return 1; });
        Assert.Equal(GateStatus.TimedOut, slow.Status);

        using var session = new CancellationTokenSource();
        var pending = gate.RunAsync("v", CompileKind.Check, ct => { Task.Delay(5000, ct).Wait(ct); return 1; }, session.Token);
        await Task.Delay(50);
        session.Cancel();
        Assert.Equal(GateStatus.Cancelled, (await pending).Status);
        Assert.True((await gate.RunAsync("w", CompileKind.Check, _ => 1)).Ok); // slot was released
        Assert.Equal(0, gate.Snapshot().Active);
    }

    [Fact]
    public async Task Live_check_is_diagnostics_only_and_run_still_produces_the_assembly()
    {
        var problem = PlaygroundCatalog.Find("two-sum")!;
        using var gate = Gate(concurrent: 2, queued: 4, checkWait: 5, runWait: 5);
        var service = new PlaygroundService(new BrowserModeExecutor("x"), new SandboxOptions(), gate);
        string code = Solutions.BySlug["two-sum"];
        var check = await service.CheckAsync(problem, code, "u");
        Assert.True(check.Ok);
        Assert.True(check.Value!.Success);
        Assert.Null(check.Value.Assembly);           // no instrumentation / emit while typing
        Assert.NotNull(check.Value.MethodName);      // advice still works
        Assert.NotNull(CodeCompiler.Compile(code, problem.Entry).Assembly);

        var broken = await service.CheckAsync(problem, code.Replace("return", "retrun"), "u");
        Assert.False(broken.Value!.Success);
        Assert.NotEmpty(broken.Value.Errors);
    }
}
