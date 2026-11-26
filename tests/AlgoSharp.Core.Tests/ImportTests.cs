using System.Text.Json;
using AlgoSharp.Playground;
using AlgoSharp.Playground.Library;
using AlgoSharp.Runner;

namespace AlgoSharp.Core.Tests;

public class ImportTests
{
    internal const string Lesson = """
        Lesson 4: Neighbor Comparisons

        Level 12: Count Increasing Adjacent Pairs
        Concepts: Arrays, Loops, Neighbor Comparison
        Difficulty: Easy
        Given an array of integers, count how many adjacent pairs (nums[i], nums[i + 1]) are strictly increasing.
        Input: [1, 3, 2, 4, 5]
        Output: 3
        Explanation: (1, 3), (2, 4) and (4, 5) increase.

        Level 13: Count Decreasing Adjacent Pairs
        Concepts: Arrays, Loops, Neighbor Comparison
        Count how many adjacent pairs are strictly decreasing.
        Input: [5, 3, 4, 2, 1]
        Output: 3

        Level 14: Count Local Maximums
        Concepts: Arrays, Loops, Multiple Conditions, Neighbor Comparison
        Count the elements that are strictly greater than both of their neighbors.
        Input: [2, 7, 3, 9, 4, 8, 1]
        Output: 3
        """;

    [Fact]
    public void Lesson_with_three_levels_becomes_three_problems()
    {
        var result = ProblemImporter.Import(Lesson);
        Assert.Equal("text", result.Kind);
        Assert.Equal("Lesson 4: Neighbor Comparisons", result.Collection);
        Assert.Equal(3, result.Problems.Count);

        var p = result.Problems.Select(x => x.Problem).ToList();
        Assert.Equal(["Count Increasing Adjacent Pairs", "Count Decreasing Adjacent Pairs", "Count Local Maximums"], p.Select(x => x.Title));
        Assert.Equal([12, 13, 14], p.Select(x => x.Level!.Value));
        Assert.Equal("Easy", p[0].Difficulty);
        Assert.Null(p[1].Difficulty); // not stated in the source → not invented
        Assert.Equal(["Arrays", "Loops", "Multiple Conditions", "Neighbor Comparison"], p[2].Concepts);
        Assert.Equal("Count the elements that are strictly greater than both of their neighbors.", p[2].Description);
        Assert.Equal("[2, 7, 3, 9, 4, 8, 1]", p[2].Examples.Single().Input);
        Assert.Equal("3", p[2].Examples.Single().Output);
        Assert.Equal("(1, 3), (2, 4) and (4, 5) increase.", p[0].Examples.Single().Explanation);

        // Signature is suggested from the example and flagged; tests come from examples and are unverified.
        Assert.Equal("public int CountLocalMaximums(int[] nums)", p[2].Signature);
        Assert.True(p[2].SignatureSuggested);
        Assert.All(p, x => Assert.All(x.Tests, t => Assert.False(t.Verified)));
        Assert.Equal(["[2, 7, 3, 9, 4, 8, 1]"], p[2].Tests.Single().Args);
        Assert.Equal(ProblemMode.Practice, p[2].Mode);
        Assert.True(result.Ok);
    }

    [Fact]
    public void Single_exercise_and_markdown_formats_are_understood()
    {
        const string md = """
            ### **Level 7: Sum of Evens (Easy)**
            **Concepts:** Arrays, Conditions
            **Task:** Return the sum of the even numbers.

            **Example 1:**
            Input: nums = [1, 2, 3, 4]
            Output: 6

            **Example 2:**
            - Input: nums = [] → Output: 0

            **Constraints:**
            - 0 <= nums.length <= 100
            - -1000 <= nums[i] <= 1000
            """;
        var p = ProblemImporter.Import(md).Problems.Single().Problem;
        Assert.Equal("Sum of Evens", p.Title);
        Assert.Equal("Easy", p.Difficulty);
        Assert.Equal(7, p.Level);
        Assert.Equal("Return the sum of the even numbers.", p.Description);
        Assert.Equal(2, p.Examples.Count);
        Assert.Equal("nums = []", p.Examples[1].Input);
        Assert.Equal("0", p.Examples[1].Output);
        Assert.Equal(2, p.Constraints.Count);
        Assert.Equal("public int SumOfEvens(int[] nums)", p.Signature);
    }

    [Fact]
    public void Text_without_title_or_description_is_flagged_not_invented()
    {
        var result = ProblemImporter.Import("Input: [1, 2]\nOutput: 3");
        var issues = result.Problems.Single().Issues;
        Assert.Contains(issues, i => i.Blocking && i.Path.Contains("title"));
        Assert.Contains(issues, i => i.Blocking && i.Path.Contains("description"));
        Assert.Equal("", result.Problems.Single().Problem.Title);
    }

    [Fact]
    public void AlgoSharp_json_example_imports_cleanly_and_the_prompt_matches_the_schema()
    {
        var result = ProblemImporter.Import(ImportSchema.ExampleJson());
        Assert.Equal("json", result.Kind);
        Assert.True(result.Ok);
        var imported = result.Problems.Single();
        Assert.Empty(imported.Issues);
        var p = imported.Problem;
        Assert.Equal("Count Local Maximums", p.Title);
        Assert.Equal(14, p.Level);
        Assert.Equal("Lesson 4: Neighbor Comparisons", p.Collection);
        Assert.Equal("public int CountLocalMaximums(int[] nums)", p.Signature);
        Assert.Equal(2, p.Tests.Count);           // one from the example's args, one explicit test
        Assert.All(p.Tests, t => Assert.False(t.Verified));

        string prompt = ImportSchema.AiPrompt();
        foreach (var f in ImportSchema.Problem.Concat(ImportSchema.Example).Concat(ImportSchema.Test)) Assert.Contains($"- {f.Name} (", prompt);
        Assert.Contains(ImportSchema.ExampleJson(), prompt);
        Assert.Contains("Do not invent verified test cases", prompt);
    }

    [Fact]
    public void Json_errors_are_reported_per_field()
    {
        Assert.Contains("Invalid JSON", ProblemImporter.Import("{ \"problems\": [ }").Issues.Single().Message);

        const string bad = """
            { "format": "algosharp/problems", "version": 1, "problems": [
              { "title": "", "description": "x", "level": "high", "signature": "public void Do()", "examples": [ { "output": "1" } ],
                "tests": [ { "args": [1, 2], "expected": 3 } ], "surprise": 1 } ] }
            """;
        var issues = ProblemImporter.Import(bad).Problems.Single().Issues;
        Assert.Contains(issues, i => i.Path == "problems[0].title" && i.Blocking);
        Assert.Contains(issues, i => i.Path == "problems[0].level");
        Assert.Contains(issues, i => i.Path == "problems[0].signature" && i.Message.Contains("void"));
        Assert.Contains(issues, i => i.Path == "problems[0].examples[0].input");
        Assert.Contains(issues, i => i.Path == "problems[0].surprise");

        var future = ProblemImporter.Import("""{ "format": "algosharp/problems", "version": 99, "problems": [] }""");
        Assert.Contains(future.Issues, i => i.Blocking && i.Message.Contains("newer"));
    }

    [Fact]
    public void Json_accepts_fenced_arrays_stringified_args_and_marks_verified_tests()
    {
        const string json = """
            ```json
            [ { "title": "Is Even", "description": "Return whether n is even.", "signature": "public bool IsEven(int n)",
                "tests": [ { "args": ["4"], "expected": "true", "verified": true }, { "args": [3], "expected": false } ] } ]
            ```
            """;
        var p = ProblemImporter.Import(json).Problems.Single().Problem;
        Assert.Equal([["4"], ["3"]], p.Tests.Select(t => t.Args));
        Assert.Equal(["true", "false"], p.Tests.Select(t => t.Expected));
        Assert.Equal([true, false], p.Tests.Select(t => t.Verified));
    }

    [Fact]
    public void Challenge_requires_valid_signature_and_verified_tests()
    {
        var p = ProblemImporter.Import(Lesson).Problems[2].Problem;
        Assert.False(UserProblemAdapter.CanChallenge(p).Ok);
        var asChallenge = p with { Mode = ProblemMode.Challenge };
        Assert.Equal(ProblemMode.Practice, UserProblemAdapter.ToPlayground(asChallenge).EffectiveMode);

        var verified = asChallenge with { Tests = [.. p.Tests.Select(t => t with { Verified = true })] };
        Assert.True(UserProblemAdapter.CanChallenge(verified).Ok);
        Assert.Equal(ProblemMode.Challenge, UserProblemAdapter.ToPlayground(verified).EffectiveMode);

        var noSig = p with { Signature = null };
        var setup = UserProblemAdapter.ToPlayground(noSig);
        Assert.False(setup.HasContract);
        Assert.Contains("static void Main()", setup.Problem.StarterCode);
    }

    private static PlaygroundRun Run(PlaygroundProblem problem, string code)
    {
        var compile = PlaygroundService.Check(problem, code);
        Assert.True(compile.Success, string.Join("\n", compile.Errors.Select(e => e.Message)));
        var response = RunnerEngine.Execute(PlaygroundService.BuildRequest(problem, compile, problem.DefaultCases, 3000, 4000));
        Assert.True(response.Ok, response.Error);
        return PlaygroundService.Interpret(problem, compile, code, problem.DefaultCases, response);
    }

    [Fact]
    public void Imported_level_14_runs_graded_in_challenge_mode_with_the_users_own_code()
    {
        var p = ProblemImporter.Import(Lesson).Problems[2].Problem;
        var verified = p with
        {
            Mode = ProblemMode.Challenge,
            Tests = [.. p.Tests.Select(t => t with { Verified = true }), new UserTest { Args = ["[1, 2, 3]"], Expected = "0", Verified = true }],
        };
        var setup = UserProblemAdapter.ToPlayground(verified);
        const string code = """
            public class Solution
            {
                public int CountLocalMaximums(int[] values)
                {
                    int peaks = 0;
                    for (int p = 1; p + 1 < values.Length; p++)
                    {
                        if (values[p] > values[p - 1] && values[p] > values[p + 1]) peaks++;
                    }
                    return peaks;
                }
            }
            """;
        var run = Run(setup.Problem, code);
        Assert.Equal(2, run.Cases.Count);
        Assert.All(run.Cases, c => Assert.True(c.Verdict!.Graded && c.Verdict.Passed, c.Verdict.Explanation));
        Assert.Contains(run.Cases[0].Frames.SelectMany(f => f.Containers).Where(c => c.Array is not null).SelectMany(c => c.Array!.Pointers), x => x.Name == "p");

        const string wrong = "public class Solution { public int CountLocalMaximums(int[] values) { return values.Length; } }";
        var failed = Run(setup.Problem, wrong);
        Assert.All(failed.Cases, c => Assert.True(c.Verdict!.Graded && !c.Verdict.Passed));
    }

    [Fact]
    public void Practice_mode_runs_ungraded_and_reports_the_source_expected_output()
    {
        var p = ProblemImporter.Import(Lesson).Problems[0].Problem;
        var setup = UserProblemAdapter.ToPlayground(p);
        Assert.Equal(ProblemMode.Practice, setup.EffectiveMode);
        const string code = "public class Solution { public int CountIncreasingAdjacentPairs(int[] nums) { int c = 0; for (int i = 0; i + 1 < nums.Length; i++) if (nums[i] < nums[i + 1]) c++; return c; } }";
        var run = Run(setup.Problem, code);
        var v = run.Cases.Single().Verdict!;
        Assert.False(v.Graded);
        Assert.True(v.Passed);
        Assert.Contains("not verified", v.Explanation);

        var main = UserProblemAdapter.ToPlayground(p with { Signature = null });
        var mainRun = Run(main.Problem, "public class Program { public static void Main() { int[] a = { 1, 3, 2 }; Console.WriteLine(a[1]); } }");
        Assert.Equal("3\n", mainRun.Cases.Single().Result!.Output.Replace("\r\n", "\n"));
        Assert.False(mainRun.Cases.Single().Verdict!.Graded);
    }

    [Fact]
    public void Library_records_round_trip_through_json_for_backup()
    {
        var p = ProblemImporter.Import(Lesson).Problems[0].Problem;
        var backup = new LibraryBackup { Problems = [p], Progress = [new ProblemProgress { ProblemId = p.Id, Status = ProblemStatus.InProgress, Draft = "code" }] };
        string json = JsonSerializer.Serialize(backup, LibraryJson.Options);
        var back = JsonSerializer.Deserialize<LibraryBackup>(json, LibraryJson.Options)!;
        Assert.Equal(p.Title, back.Problems.Single().Title);
        Assert.Equal(p.Tests.Count, back.Problems.Single().Tests.Count);
        Assert.Equal(ProblemStatus.InProgress, back.Progress.Single().Status);
        Assert.Contains("\"status\":\"InProgress\"", json);
    }
}
