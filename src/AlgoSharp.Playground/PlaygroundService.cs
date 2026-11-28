using System.Text.Json;
using AlgoSharp.Playground.Compilation;
using AlgoSharp.Playground.Execution;
using AlgoSharp.Tracing;

namespace AlgoSharp.Playground;

public sealed record CaseOutcome(
    PlaygroundCase Case,
    CaseResult? Result,
    Verdict? Verdict,
    IReadOnlyList<TraceFrame> Frames);

public sealed record PlaygroundRun(
    CompileResult Compile,
    string? ExecutionError,
    IReadOnlyList<CaseOutcome> Cases,
    Advice? Advice)
{
    /// <summary>Set when the server turned the run away (busy, cooldown, timeout): nothing was compiled or run.</summary>
    public GateStatus? Rejected { get; init; }
}

/// <summary>Compile → (sandbox) execute → check → build visual frames → advise.</summary>
public sealed class PlaygroundService(ICodeExecutor executor, SandboxOptions options, CompileGate? gate = null)
{
    public Task<SandboxStatus> StatusAsync(CancellationToken ct = default) => executor.GetStatusAsync(ct);

    /// <summary>Compiles and analyzes only. Never executes anything.</summary>
    public static CompileResult Check(PlaygroundProblem problem, string code) => CodeCompiler.Compile(code, problem.Entry);

    /// <summary>
    /// The live as-you-type check: diagnostics only (no instrumentation or emit), through the compile gate.
    /// Older checks from the same user are dropped in favour of the newest code.
    /// </summary>
    public Task<GateResult<CompileResult>> CheckAsync(PlaygroundProblem problem, string code, string user, CancellationToken ct = default) =>
        Gated(user, CompileKind.Check, token => CodeCompiler.Compile(code, problem.Entry, diagnosticsOnly: true, token), ct);

    private async Task<GateResult<T>> Gated<T>(string user, CompileKind kind, Func<CancellationToken, T> work, CancellationToken ct) =>
        gate is null ? new(GateStatus.Ok, work(ct), null) : await gate.RunAsync(user, kind, work, ct);

    /// <summary>Builds the sandbox request (used by the service and by tests).</summary>
    public static RunRequest BuildRequest(PlaygroundProblem problem, CompileResult compile, IReadOnlyList<PlaygroundCase> cases, int maxEvents, int maxOutput) =>
        new(Convert.ToBase64String(compile.Assembly!),
            compile.TypeName!,
            compile.MethodName!,
            cases.Select(c => new TestCaseRequest(c.Label, problem.RunnerArgs(problem.Parse(c.Args)))).ToList(),
            maxEvents,
            maxOutput,
            problem.Entry.DesignClass is null ? null : compile.MethodName);

    /// <param name="caller">Opaque per-account id; executors with per-account quotas require it.</param>
    public Task<PlaygroundRun> RunAsync(PlaygroundProblem problem, string code, IReadOnlyList<PlaygroundCase> cases, string? caller = null, CancellationToken ct = default) =>
        RunWithAsync(problem, code, cases, (request, token) => executor is ICallerAwareExecutor perCaller && caller is not null
            ? perCaller.RunAsync(request, caller, token)
            : executor.RunAsync(request, token), caller, ct);

    /// <summary>
    /// Same pipeline with a caller-supplied execution step, used for browser execution where the page's own
    /// Web Worker runs the compiled assembly. Compilation, checking and frame building stay on the server.
    /// </summary>
    public async Task<PlaygroundRun> RunWithAsync(PlaygroundProblem problem, string code, IReadOnlyList<PlaygroundCase> cases,
        Func<RunRequest, CancellationToken, Task<SandboxResult>> execute, string? user = null, CancellationToken ct = default)
    {
        // Only the compilation holds a compile slot; running (in the browser or a sandbox) happens after it's released.
        var gated = await Gated(user ?? "anonymous", CompileKind.Run, token => CodeCompiler.Compile(code, problem.Entry, ct: token), ct);
        if (!gated.Ok || gated.Value is null)
            return new(new CompileResult(false, [], null, null, null, null), gated.Message ?? "The run was cancelled.", [], null) { Rejected = gated.Status };
        var compile = gated.Value;
        if (!compile.Success) return new(compile, null, [], null);

        var request = BuildRequest(problem, compile, cases, options.MaxEvents, options.MaxOutputChars);
        compile = compile with { Assembly = null }; // the request carries it now; don't keep a second copy per session
        var sandbox = await execute(request, ct);
        if (!sandbox.Ok || sandbox.Response is null) return new(compile, sandbox.Error ?? "Execution failed.", [], Advise(problem, compile, []));
        if (!sandbox.Response.Ok) return new(compile, sandbox.Response.Error, [], Advise(problem, compile, []));
        try
        {
            return Interpret(problem, compile, code, cases, sandbox.Response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IndexOutOfRangeException or KeyNotFoundException or FormatException or System.Text.Json.JsonException)
        {
            // Browser results are untrusted input: a malformed trace must not take the circuit down.
            return new(compile, "The run finished, but its trace couldn't be displayed.", [], Advise(problem, compile, []));
        }
    }

    /// <summary>Turns a runner response into verdicts, frames and advice. Public so it can be tested without a sandbox.</summary>
    public static PlaygroundRun Interpret(PlaygroundProblem problem, CompileResult compile, string code, IReadOnlyList<PlaygroundCase> cases, RunResponse response)
    {
        var lines = code.Replace("\r\n", "\n").Split('\n');
        var outcomes = new List<CaseOutcome>();
        for (int i = 0; i < cases.Count; i++)
        {
            var parsed = problem.Parse(cases[i].Args);
            var result = i < response.Cases.Count ? response.Cases[i] : null;
            string? expected = problem.Checker is null ? problem.Reference(parsed)?.ToJsonString() : null;
            Verdict verdict = result switch
            {
                null => new(false, "No result was returned for this case.", expected),
                { LimitReached: true } => new(false, result.ErrorMessage ?? "Step limit reached.", expected ?? problem.Check(parsed, null).Expected),
                { Completed: false } => new(false, $"{result.ErrorType}: {result.ErrorMessage}", expected ?? problem.Check(parsed, null).Expected),
                _ => problem.Check(parsed, result.ResultJson),
            };
            var frames = result is null ? [] : TraceViewBuilder.Build(result, lines);
            outcomes.Add(new CaseOutcome(cases[i], result, verdict, frames));
        }
        return new(compile, null, outcomes, Advise(problem, compile, outcomes));
    }

    private static Advice? Advise(PlaygroundProblem problem, CompileResult compile, IReadOnlyList<CaseOutcome> outcomes)
    {
        if (compile.Tree is null || compile.MethodName is null) return null;
        var runs = outcomes
            .Where(o => o.Result is not null)
            .Select(o => (ProblemInputs.SizeOf(problem.Parse(o.Case.Args)), o.Result!.Events.Count(e => e.Kind == TraceEventKind.Condition), o.Verdict?.Passed == true))
            .ToList();
        return CodeAdvisor.Analyze(compile.Tree, compile.MethodName, runs, problem.Hints.Count);
    }
}
