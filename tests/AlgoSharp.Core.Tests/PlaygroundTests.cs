using AlgoSharp.Core.Problems;
using AlgoSharp.Playground;
using AlgoSharp.Playground.Compilation;
using AlgoSharp.Runner;
using AlgoSharp.Tracing;

namespace AlgoSharp.Core.Tests;

/// <summary>
/// End-to-end tests of compile → instrument → run → interpret. The runner is called in-process
/// here only with test-authored code; the web app always goes through the sandbox executor.
/// </summary>
public class PlaygroundTests
{
    private static readonly PlaygroundProblem P = PlaygroundCatalog.TwoSum;

    private static PlaygroundRun Run(string code, params ProblemInput[] inputs)
    {
        var cases = (inputs.Length == 0 ? P.DefaultCases.Select(c => c.Input) : inputs)
            .Select((input, i) => new PlaygroundCase($"case {i}", input)).ToList();
        var compile = PlaygroundService.Check(P, code);
        Assert.True(compile.Success, string.Join("\n", compile.Errors.Select(e => $"{e.Line}: {e.Message}")));
        var request = new RunRequest(Convert.ToBase64String(compile.Assembly!), compile.TypeName!, compile.MethodName!,
            cases.Select(c => new TestCaseRequest(c.Label, P.ToArgs(c.Input))).ToList(), 2000, 4000);
        var response = RunnerEngine.Execute(request);
        Assert.True(response.Ok, response.Error);
        return PlaygroundService.Interpret(P, compile, code, cases, response);
    }

    private const string HashMapWithCustomNames = """
        using System.Collections.Generic;

        public class Solution
        {
            public int[] TwoSum(int[] values, int goal)
            {
                var lookup = new Dictionary<int, int>();
                for (int idx = 0; idx < values.Length; idx++)
                {
                    int need = goal - values[idx];
                    if (lookup.TryGetValue(need, out int partner))
                    {
                        return new[] { partner, idx };
                    }
                    lookup[values[idx]] = idx;
                }
                return new int[0];
            }
        }
        """;

    [Fact]
    public void Starter_brute_force_passes_and_traces_real_conditions()
    {
        var run = Run(P.StarterCode);
        Assert.All(run.Cases, c => Assert.True(c.Verdict!.Passed, c.Verdict.Explanation));

        var first = run.Cases[0].Result!;
        var cond = first.Events.Where(e => e.Kind == TraceEventKind.Condition).ToList();
        Assert.Contains(cond, e => e.Text == "nums[i] + nums[j] == target" && e.Result == true);
        Assert.Contains(cond, e => e.Text == "i < nums.Length" && e.Result == true);
        // Reads are attributed to the user's own index variables.
        Assert.Contains(first.Events.SelectMany(e => e.Accesses), a => a.Kind == "read" && a.IndexName == "j" && a.Key == "1");
        var returned = first.Events.Single(e => e.Kind == TraceEventKind.Return);
        Assert.Equal("[0, 1]", returned.Text);
        Assert.Equal("[0,1]", first.ResultJson);
    }

    [Fact]
    public void Custom_variable_names_and_dictionary_lookups_are_traced()
    {
        var run = Run(HashMapWithCustomNames, new([2, 7, 11, 15], 9), new([3, 3], 6), new([1, 2], 7));
        Assert.All(run.Cases, c => Assert.True(c.Verdict!.Passed, c.Verdict.Explanation));

        var events = run.Cases[0].Result!.Events;
        var names = events.SelectMany(e => e.Vars).Select(v => v.Name).ToHashSet();
        Assert.Superset(new HashSet<string> { "values", "goal", "lookup", "idx", "need", "partner" }, names);
        Assert.DoesNotContain("i", names);
        var probes = events.SelectMany(e => e.Accesses).Where(a => a.Kind == "probe").ToList();
        Assert.Contains(probes, a => a.Key == "7" && a.Hit == false);
        Assert.Contains(probes, a => a.Key == "2" && a.Hit == true);

        // The frame for the successful lookup shows the dictionary with key 2 found.
        var frame = run.Cases[0].Frames.First(f => f.Containers.Any(c => c.Map?.ProbeHit == true));
        Assert.Contains(frame.Containers.Single(c => c.Map is not null).Map!.Entries, e => e.Key == "2" && e.Value == "0");
        // Array pointers come from the index variable actually used: idx.
        Assert.Contains(run.Cases[0].Frames.SelectMany(f => f.Containers).Where(c => c.Array is not null).SelectMany(c => c.Array!.Pointers), p => p.Name == "idx");
        Assert.Equal("Hash map lookup", run.Advice!.Approach);
    }

    [Fact]
    public void Runtime_exception_reports_type_line_and_history()
    {
        const string code = """
            public class Solution
            {
                public int[] TwoSum(int[] nums, int target)
                {
                    for (int i = 0; i <= nums.Length; i++)
                    {
                        int x = nums[i];
                    }
                    return new int[0];
                }
            }
            """;
        var run = Run(code, new ProblemInput([1, 2, 3], 100));
        var result = run.Cases[0].Result!;
        Assert.False(result.Completed);
        Assert.Equal("IndexOutOfRangeException", result.ErrorType);
        Assert.Equal(7, result.ErrorLine);
        Assert.False(run.Cases[0].Verdict!.Passed);
        var last = result.Events[^1];
        Assert.Equal(TraceEventKind.Exception, last.Kind);
        Assert.Contains(last.Vars, v => v.Name == "i" && v.Value.Text == "3");
        Assert.True(result.Events.Count > 5);
    }

    [Fact]
    public void Infinite_loop_hits_the_step_limit_even_when_exceptions_are_swallowed()
    {
        const string code = """
            public class Solution
            {
                public int[] TwoSum(int[] nums, int target)
                {
                    int k = 0;
                    while (true)
                    {
                        try { k++; } catch (System.Exception) { k = 0; }
                    }
                }
            }
            """;
        var run = Run(code, new ProblemInput([1, 2], 3));
        var result = run.Cases[0].Result!;
        Assert.True(result.LimitReached);
        Assert.Equal("TraceLimitException", result.ErrorType);
        Assert.Contains("infinite loop", result.ErrorMessage);
    }

    [Fact]
    public void Wrong_answer_is_reported_by_the_checker()
    {
        const string code = """
            public class Solution
            {
                public int[] TwoSum(int[] nums, int target) => new[] { 0, 0 };
            }
            """;
        var run = Run(code, new ProblemInput([2, 7], 9));
        Assert.False(run.Cases[0].Verdict!.Passed);
        Assert.Contains("twice", run.Cases[0].Verdict!.Explanation);
    }

    [Theory]
    [InlineData("public class Solution { public int[] TwoSum(int[] nums, int target) { System.IO.File.ReadAllText(\"/etc/passwd\"); return null; } }", "System.IO")]
    [InlineData("public class Solution { public int[] TwoSum(int[] nums, int target) { System.Environment.Exit(1); return null; } }", "System.Environment")]
    [InlineData("public class Solution { public int[] TwoSum(int[] nums, int target) { var t = typeof(int).Assembly; return null; } }", "System.Reflection")]
    [InlineData("public class Solution { public int[] TwoSum(int[] nums, int target) { goto end; end: return null; } }", "goto")]
    [InlineData("public class Solution { public int[] TwoSum(int[] nums, int target) { return nums.Lenght; } }", "Lenght")]
    [InlineData("public class Solution { public int Add(int a, int b) => a + b; }", "int[] TwoSum")]
    public void Compile_rejects_errors_blocked_apis_and_missing_entry(string code, string expected)
    {
        var compile = PlaygroundService.Check(P, code);
        Assert.False(compile.Success);
        Assert.Contains(compile.Errors, e => e.Message.Contains(expected));
        Assert.Null(compile.Assembly);
    }

    [Fact]
    public void Any_method_name_with_the_right_signature_is_accepted()
    {
        const string code = """
            public static class Finder
            {
                public static int[] FindPair(int[] a, int t)
                {
                    var seen = new HashSet<int>();
                    for (int k = 0; k < a.Length; k++)
                    {
                        if (seen.Contains(t - a[k]))
                        {
                            return new[] { Array.IndexOf(a, t - a[k]), k };
                        }
                        seen.Add(a[k]);
                    }
                    return new int[0];
                }
            }
            """;
        var run = Run(code, new ProblemInput([2, 7, 11, 15], 9));
        Assert.True(run.Cases[0].Verdict!.Passed);
        Assert.Contains(run.Cases[0].Frames.SelectMany(f => f.Containers), c => c.Map is not null);
    }

    [Fact]
    public void Advisor_labels_brute_force_as_an_estimate_and_never_claims_optimal()
    {
        var run = Run(P.StarterCode);
        var advice = run.Advice!;
        Assert.Equal("Brute force (nested loops)", advice.Approach);
        Assert.StartsWith("O(n²)", advice.Time);
        Assert.Contains("estimate", advice.Confidence);
        Assert.DoesNotContain(advice.Suggestions, s => s.Contains("optimal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Instrumented_code_keeps_semantics_for_tricky_constructs()
    {
        const string code = """
            public class Solution
            {
                public int[] TwoSum(int[] nums, int target)
                {
                    int[] copy = (int[])nums.Clone();
                    (copy[0], copy[1]) = (copy[1], copy[0]);
                    copy[0]++;
                    copy[0]--;
                    foreach (var (v, k) in copy.Select((v, k) => (v, k)))
                    {
                        if (v < 0) continue;
                    }
                    switch (copy.Length)
                    {
                        case 0: return new int[0];
                        default: break;
                    }
                    int a = 0, b = nums.Length - 1;
                    var order = Enumerable.Range(0, nums.Length).OrderBy(i => nums[i]).ToArray();
                    while (a < b)
                    {
                        int s = nums[order[a]] + nums[order[b]];
                        if (s == target) return new[] { order[a], order[b] };
                        else if (s < target) a++;
                        else b--;
                    }
                    return new int[0];
                }
            }
            """;
        var rng = new Random(5);
        for (int t = 0; t < 30; t++)
        {
            var input = ProblemCatalog.TwoSumProblem.RandomInput(rng);
            var run = Run(code, input);
            Assert.True(run.Cases[0].Verdict!.Passed, $"{string.Join(",", input.Numbers)} / {input.Target}: {run.Cases[0].Verdict!.Explanation}");
        }
    }
}
