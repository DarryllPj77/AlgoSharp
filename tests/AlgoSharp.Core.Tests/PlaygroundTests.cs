using System.Text.Json.Nodes;
using AlgoSharp.Playground;
using AlgoSharp.Runner;
using AlgoSharp.Tracing;

namespace AlgoSharp.Core.Tests;

/// <summary>
/// End-to-end tests of compile → instrument → run → interpret. The runner is called in-process
/// here only with test-authored code; the web app always goes through the sandbox executor.
/// </summary>
[Collection(TracerCollection.Name)]
public class PlaygroundTests
{
    private static PlaygroundProblem Problem(string slug) => PlaygroundCatalog.Find(slug)!;

    internal static PlaygroundRun Run(PlaygroundProblem p, string code, IReadOnlyList<PlaygroundCase>? cases = null)
    {
        cases ??= p.DefaultCases;
        var compile = PlaygroundService.Check(p, code);
        Assert.True(compile.Success, p.Slug + ": " + string.Join("\n", compile.Errors.Select(e => $"{e.Line}: {e.Message}")));
        var response = RunnerEngine.Execute(PlaygroundService.BuildRequest(p, compile, cases, 3000, 4000));
        Assert.True(response.Ok, response.Error);
        return PlaygroundService.Interpret(p, compile, code, cases, response);
    }

    private static PlaygroundCase Case(params string[] args) => new("case", args);

    public static TheoryData<string> Slugs => [.. PlaygroundCatalog.All.Select(p => p.Slug)];

    [Fact]
    public void Catalog_has_all_28_easy_problems_with_unique_slugs()
    {
        Assert.Equal(28, PlaygroundCatalog.All.Count);
        Assert.Equal(28, PlaygroundCatalog.All.Select(p => p.Slug).Distinct().Count());
        Assert.All(PlaygroundCatalog.All, p => Assert.Contains(p.Category, PlaygroundCatalog.Categories));
        Assert.Equal(PlaygroundCatalog.All.Select(p => p.Slug).Order(), Solutions.BySlug.Keys.Order());
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public void Starter_compiles_and_examples_match_reference(string slug)
    {
        var p = Problem(slug);
        Assert.True(PlaygroundService.Check(p, p.StarterCode).Success, string.Join("\n", PlaygroundService.Check(p, p.StarterCode).Errors.Select(e => e.Message)));
        foreach (var example in p.Examples)
        {
            var expected = JsonNode.Parse(example.Output.Replace(" ", ""));
            var reference = p.Reference(p.Parse(example.Args));
            Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(reference!.ToJsonString())), $"{slug}: example {string.Join(", ", example.Args)} shows {example.Output}, reference gives {reference.ToJsonString()}");
        }
        foreach (var test in p.ExtraTests) p.Parse(test); // all test inputs are valid
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public void A_correct_user_solution_passes_every_case(string slug)
    {
        var p = Problem(slug);
        var run = Run(p, Solutions.BySlug[slug]);
        Assert.All(run.Cases, c => Assert.True(c.Verdict!.Passed, $"{slug} / {p.Describe(c.Case.Args)}: {c.Verdict.Explanation}"));
        Assert.All(run.Cases, c => Assert.NotEmpty(c.Frames));
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public void The_starter_code_runs_and_is_reported_honestly(string slug)
    {
        var p = Problem(slug);
        var run = Run(p, p.StarterCode);
        Assert.Equal(p.DefaultCases.Count, run.Cases.Count);
        Assert.All(run.Cases, c => Assert.False(string.IsNullOrEmpty(c.Verdict!.Explanation)));
        Assert.Contains(run.Cases, c => !c.Verdict!.Passed); // stubs don't solve the problem
    }

    [Fact]
    public void Two_sum_trace_uses_the_users_own_names_and_records_lookups()
    {
        const string code = """
            public class Solution
            {
                public int[] TwoSum(int[] values, int goal)
                {
                    var lookup = new Dictionary<int, int>();
                    for (int idx = 0; idx < values.Length; idx++)
                    {
                        int need = goal - values[idx];
                        if (lookup.TryGetValue(need, out int partner)) return new[] { partner, idx };
                        lookup[values[idx]] = idx;
                    }
                    return new int[0];
                }
            }
            """;
        var run = Run(Problem("two-sum"), code, [Case("[2, 7, 11, 15]", "9")]);
        Assert.True(run.Cases[0].Verdict!.Passed);
        var events = run.Cases[0].Result!.Events;
        Assert.Superset(new HashSet<string> { "values", "goal", "lookup", "idx", "need", "partner" }, events.SelectMany(e => e.Vars).Select(v => v.Name).ToHashSet());
        Assert.Contains(events.SelectMany(e => e.Accesses), a => a.Kind == "probe" && a.Key == "2" && a.Hit == true);
        Assert.Contains(run.Cases[0].Frames.SelectMany(f => f.Containers).Where(c => c.Array is not null).SelectMany(c => c.Array!.Pointers), p => p.Name == "idx");
        Assert.Equal("Hash map lookup", run.Advice!.Approach);
    }

    [Fact]
    public void Reverse_list_trace_shows_nodes_pointers_and_rewiring()
    {
        var run = Run(Problem("reverse-linked-list"), Solutions.BySlug["reverse-linked-list"], [Case("[1, 2, 3]")]);
        var graphs = run.Cases[0].Frames.SelectMany(f => f.Containers).Select(c => c.Graph).OfType<AlgoSharp.Core.Engine.GraphFrame>().ToList();
        Assert.NotEmpty(graphs);
        Assert.Contains(graphs, g => g.Pointers.Any(p => p.Name == "curr") && g.Pointers.Any(p => p.Name == "prev"));
        Assert.Contains(graphs, g => g.Edges.Any(e => e.State == AlgoSharp.Core.Engine.CellState.Active));
        Assert.Equal("[3,2,1]", run.Cases[0].Result!.ResultJson);
    }

    [Fact]
    public void Tree_and_string_and_stack_visuals_come_from_the_trace()
    {
        var tree = Run(Problem("invert-binary-tree"), Solutions.BySlug["invert-binary-tree"], [Case("[4, 2, 7]")]);
        Assert.Contains(tree.Cases[0].Frames.SelectMany(f => f.Containers), c => c.Graph is { Nodes.Count: 3 } && c.Title.StartsWith("root"));

        var pal = Run(Problem("valid-palindrome"), Solutions.BySlug["valid-palindrome"], [Case("\"ab a\"")]);
        Assert.Contains(pal.Cases[0].Frames.SelectMany(f => f.Containers), c => c.Title.StartsWith("s") && c.Array is { Cells.Count: 4 });

        var par = Run(Problem("valid-parentheses"), Solutions.BySlug["valid-parentheses"], [Case("\"([{\"")]);
        Assert.Contains(par.Cases[0].Frames.SelectMany(f => f.Containers), c => c.Array is { Layout: AlgoSharp.Core.Engine.ArrayLayout.Stack, Cells.Count: 3 });
    }

    [Fact]
    public void Design_problem_fields_are_traced()
    {
        var run = Run(Problem("kth-largest-element-in-a-stream"), Solutions.BySlug["kth-largest-element-in-a-stream"], [Case("2", "[1]", "[5, 3]")]);
        Assert.True(run.Cases[0].Verdict!.Passed, run.Cases[0].Verdict!.Explanation);
        Assert.Contains(run.Cases[0].Result!.Events.SelectMany(e => e.Vars), v => v.Name == "this.heap" && v.Value.Kind == "heap");
    }

    [Fact]
    public void Wrong_answer_shows_expected_and_actual()
    {
        const string code = "public class Solution { public int MaxProfit(int[] prices) { return prices[0]; } }";
        var run = Run(Problem("best-time-to-buy-and-sell-stock"), code, [Case("[7, 1, 5, 3, 6, 4]")]);
        var v = run.Cases[0].Verdict!;
        Assert.False(v.Passed);
        Assert.Equal("5", v.Expected);
        Assert.Contains("Returned 7", v.Explanation);
    }

    [Fact]
    public void Runtime_exception_reports_type_line_and_variables()
    {
        const string code = """
            public class Solution
            {
                public int MissingNumber(int[] nums)
                {
                    int total = 0;
                    for (int k = 0; k <= nums.Length; k++)
                    {
                        total += nums[k];
                    }
                    return total;
                }
            }
            """;
        var run = Run(Problem("missing-number"), code, [Case("[3, 0, 1]")]);
        var result = run.Cases[0].Result!;
        Assert.Equal("IndexOutOfRangeException", result.ErrorType);
        Assert.Equal(8, result.ErrorLine);
        Assert.Contains(result.Events[^1].Vars, v => v.Name == "k" && v.Value.Text == "3");
        Assert.False(run.Cases[0].Verdict!.Passed);
    }

    [Fact]
    public void Null_reference_and_infinite_loops_are_reported()
    {
        const string nre = "public class Solution { public int MaxDepth(TreeNode root) { return root.left.val; } }";
        var run = Run(Problem("maximum-depth-of-binary-tree"), nre, [Case("[1]")]);
        Assert.Equal("NullReferenceException", run.Cases[0].Result!.ErrorType);

        const string loop = "public class Solution { public bool IsHappy(int n) { while (n != 1) { try { n = n * 1; } catch (Exception) { } } return true; } }";
        var looped = Run(Problem("happy-number"), loop, [Case("2")]);
        Assert.True(looped.Cases[0].Result!.LimitReached);
    }

    [Fact]
    public void Invalid_custom_inputs_are_rejected_with_messages()
    {
        Assert.Throws<FormatException>(() => Problem("binary-search").Parse(["[3, 1, 2]", "1"]));
        Assert.Throws<FormatException>(() => Problem("two-sum").Parse(["[1, x]", "2"]));
        Assert.Throws<FormatException>(() => Problem("linked-list-cycle").Parse(["[1, 2]", "5"]));
        Assert.Throws<FormatException>(() => Problem("valid-parentheses").Parse(["(a)"]));
        Assert.Equal("hello", Problem("valid-palindrome").Parse(["hello"])[0].GetString()); // quotes optional
    }

    [Theory]
    [InlineData("public class Solution { public bool IsHappy(int n) { System.IO.File.ReadAllText(\"/etc/passwd\"); return true; } }", "System.IO")]
    [InlineData("public class Solution { public bool IsHappy(int n) { System.Environment.Exit(1); return true; } }", "System.Environment")]
    [InlineData("public class Solution { public bool IsHappy(int n) { var t = typeof(int).Assembly; return true; } }", "System.Reflection")]
    [InlineData("public class Solution { public bool IsHappy(int n) { goto end; end: return true; } }", "goto")]
    [InlineData("public class Solution { public int Add(int a, int b) => a + b; }", "bool IsHappy")]
    public void Compile_rejects_blocked_apis_unsupported_constructs_and_missing_entry(string code, string expected)
    {
        var compile = PlaygroundService.Check(Problem("happy-number"), code);
        Assert.False(compile.Success);
        Assert.Contains(compile.Errors, e => e.Message.Contains(expected));
    }

    [Fact]
    public void Users_can_declare_their_own_node_classes()
    {
        const string code = """
            public class ListNode { public int val; public ListNode next; public ListNode(int v = 0, ListNode n = null) { val = v; next = n; } }
            public class Solution { public ListNode ReverseList(ListNode head) { ListNode p = null; while (head != null) { var n = head.next; head.next = p; p = head; head = n; } return p; } }
            """;
        var run = Run(Problem("reverse-linked-list"), code, [Case("[1, 2]")]);
        Assert.True(run.Cases[0].Verdict!.Passed);
    }
}
