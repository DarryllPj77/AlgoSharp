using System.Text.Json;
using AlgoSharp.Playground;
using AlgoSharp.Playground.Execution;
using AlgoSharp.Runner;
using AlgoSharp.Tracing;

namespace AlgoSharp.Core.Tests;

/// <summary>
/// Browser mode: the server compiles, the visitor's WebAssembly worker runs the same RunnerEngine, and the
/// server validates what comes back. Here RunnerEngine runs in-process to produce a genuine response.
/// </summary>
[Collection(TracerCollection.Name)]
public sealed class BrowserExecutionTests
{
    private const string TwoSum = """
        public class Solution
        {
            public int[] TwoSum(int[] nums, int target)
            {
                var seenAt = new Dictionary<int, int>();
                for (int idx = 0; idx < nums.Length; idx++)
                {
                    int need = target - nums[idx];
                    if (seenAt.ContainsKey(need)) return new int[] { seenAt[need], idx };
                    seenAt[nums[idx]] = idx;
                }
                return new int[0];
            }
        }
        """;

    private static string Envelope(object value) => JsonSerializer.Serialize(value, TraceJson.Options);

    private static (PlaygroundProblem Problem, RunRequest Request) Request(string code)
    {
        var problem = PlaygroundCatalog.Find("two-sum")!;
        var compile = PlaygroundService.Check(problem, code);
        Assert.True(compile.Success);
        return (problem, PlaygroundService.BuildRequest(problem, compile, problem.DefaultCases, 2000, 8000));
    }

    [Fact]
    public async Task Server_side_browser_executor_never_runs_code()
    {
        var executor = new BrowserModeExecutor("in your browser");
        Assert.True((await executor.GetStatusAsync()).Available);
        var result = await executor.RunAsync(Request(TwoSum).Request);
        Assert.False(result.Ok);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task Browser_runner_response_flows_into_the_existing_playback()
    {
        var problem = PlaygroundCatalog.Find("two-sum")!;
        var service = new PlaygroundService(new BrowserModeExecutor("x"), new SandboxOptions());
        int calls = 0;
        var run = await service.RunWithAsync(problem, TwoSum, problem.DefaultCases, (request, _) =>
        {
            calls++;
            // What the worker does: RunnerEngine on the request JSON, response JSON back in the envelope.
            var roundTripped = JsonSerializer.Deserialize<RunRequest>(JsonSerializer.Serialize(request, TraceJson.Options), TraceJson.Options)!;
            string response = JsonSerializer.Serialize(RunnerEngine.Execute(roundTripped), TraceJson.Options);
            return Task.FromResult(BrowserExecution.Interpret(Envelope(new { status = "ok", response, ms = 5 }), request, 15));
        });

        Assert.Equal(1, calls);
        Assert.Null(run.ExecutionError);
        Assert.All(run.Cases, c => Assert.True(c.Verdict?.Passed == true, c.Verdict?.ToString()));
        var vars = run.Cases[0].Result!.Events.SelectMany(e => e.Vars).Select(v => v.Name).ToHashSet();
        Assert.Superset(new HashSet<string> { "seenAt", "idx", "need" }, vars);
        Assert.NotEmpty(run.Cases[0].Frames);
    }

    [Fact]
    public async Task Compile_errors_never_reach_the_browser()
    {
        var problem = PlaygroundCatalog.Find("two-sum")!;
        var service = new PlaygroundService(new BrowserModeExecutor("x"), new SandboxOptions());
        string code = TwoSum.Replace("return new int[0];", "System.IO.File.ReadAllText(\"x\"); return new int[0];");
        var run = await service.RunWithAsync(problem, code, problem.DefaultCases, (_, _) => throw new InvalidOperationException("must not run"));
        Assert.False(run.Compile.Success);
    }

    [Theory]
    [InlineData("timeout", "Time limit exceeded")]
    [InlineData("unsupported", "Web Workers and WebAssembly")]
    [InlineData("startup", "couldn't start")]
    [InlineData("crashed", "stopped unexpectedly")]
    [InlineData("weird", "unknown status")]
    public void Failures_are_reported_not_faked(string status, string message)
    {
        var result = BrowserExecution.Interpret(Envelope(new { status, error = "boom" }), Request(TwoSum).Request, 15);
        Assert.False(result.Ok);
        Assert.Null(result.Response);
        Assert.Contains(message, result.Error);
    }

    public static TheoryData<string> Slugs => [.. PlaygroundCatalog.All.Select(p => p.Slug)];

    [Theory]
    [MemberData(nameof(Slugs))]
    public void Every_real_catalog_trace_passes_browser_validation(string slug)
    {
        var problem = PlaygroundCatalog.Find(slug)!;
        var compile = PlaygroundService.Check(problem, Solutions.BySlug[slug]);
        var request = PlaygroundService.BuildRequest(problem, compile, problem.DefaultCases, 2000, 8000);
        string response = JsonSerializer.Serialize(RunnerEngine.Execute(request), TraceJson.Options);
        var result = BrowserExecution.Interpret(Envelope(new { status = "ok", response }), request, 15);
        Assert.True(result.Ok, result.Error);
        var run = PlaygroundService.Interpret(problem, compile, Solutions.BySlug[slug], problem.DefaultCases, result.Response!);
        Assert.All(run.Cases, c => Assert.True(c.Verdict?.Passed == true, slug + ": " + c.Verdict));
    }

    [Fact]
    public void Forged_traces_outside_tracer_limits_are_refused_not_drawn()
    {
        var request = Request(TwoSum).Request;
        SandboxResult Send(CaseResult c) => BrowserExecution.Interpret(
            Envelope(new { status = "ok", response = JsonSerializer.Serialize(new RunResponse(true, null, [c]), TraceJson.Options) }), request, 15);
        CaseResult With(params TraceVar[] vars) =>
            new("c", true, "[]", null, null, null, false, "", [new TraceEvent(0, TraceEventKind.Line, 1, "M", 0, null, null, vars, [])]);

        // A 100k-node chain dressed up as a tree: the frame builder walks it recursively, so it must never get there.
        var deep = Enumerable.Range(1, 100_000).Select(i => new TraceNode(i, "1", i + 1, 0)).ToList();
        var forged = Send(With(new TraceVar("root", new TraceValue("tree", Ref: 1, Nodes: deep))));
        Assert.False(forged.Ok);
        Assert.Contains("outside AlgoSharp's limits", forged.Error);

        Assert.False(Send(With(new TraceVar("s", new TraceValue("scalar", Text: new string('x', 100_000))))).Ok);
        Assert.False(Send(With([.. Enumerable.Range(0, 300).Select(i => new TraceVar("v" + i, new TraceValue("scalar", Text: "1")))])).Ok);
        Assert.True(Send(With(new TraceVar("n", new TraceValue("scalar", Text: "1")))).Ok);

        // Missing required fields are refused rather than crashing the circuit.
        string nulls = """{"ok":true,"cases":[{"label":"c","completed":true,"output":"","events":[{"seq":0,"kind":"Line","line":1,"method":"M","depth":0}]}]}""";
        Assert.False(BrowserExecution.Interpret(Envelope(new { status = "ok", response = nulls }), request, 15).Ok);
        Assert.False(BrowserExecution.Interpret(Envelope(new { status = "ok", response = """{"ok":true}""" }), request, 15).Ok);
    }

    [Fact]
    public void Malformed_or_oversized_answers_are_rejected_or_clipped()
    {
        var request = Request(TwoSum).Request;
        Assert.False(BrowserExecution.Interpret("not json", request, 15).Ok);
        Assert.False(BrowserExecution.Interpret(Envelope(new { status = "ok", response = "{oops" }), request, 15).Ok);

        var manyCases = new RunResponse(true, null, [.. Enumerable.Range(0, request.Cases.Count + 1)
            .Select(i => new CaseResult($"c{i}", true, "[]", null, null, null, false, "", []))]);
        Assert.False(BrowserExecution.Interpret(Envelope(new { status = "ok", response = JsonSerializer.Serialize(manyCases, TraceJson.Options) }), request, 15).Ok);

        var noisy = new RunResponse(true, null, [new CaseResult("c", true, "[]", null, null, null, false, new string('x', 100_000),
            [.. Enumerable.Range(0, 10_000).Select(i => new TraceEvent(i, TraceEventKind.Line, 1, "M", 0, null, null, [], []))])]);
        var clipped = BrowserExecution.Interpret(Envelope(new { status = "ok", response = JsonSerializer.Serialize(noisy, TraceJson.Options) }), request, 15);
        Assert.True(clipped.Ok);
        Assert.True(clipped.Response!.Cases[0].Events.Count <= request.MaxEvents + 8);
        Assert.True(clipped.Response.Cases[0].Output.Length <= request.MaxOutputChars);
    }
}
