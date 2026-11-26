using AlgoSharp.Core.Engine;
using AlgoSharp.Core.Problems;

namespace AlgoSharp.Core.Tests;

public class TwoSumTests
{
    private static readonly ProblemDef P = ProblemCatalog.TwoSumProblem;

    private static bool IsValid(ProblemInput input, int[] r) =>
        r.Length == 2 && r[0] != r[1] && input.Numbers[r[0]] + input.Numbers[r[1]] == input.Target;

    private static bool HasPair(ProblemInput input)
    {
        var a = input.Numbers;
        for (int i = 0; i < a.Length; i++) for (int j = i + 1; j < a.Length; j++) if (a[i] + a[j] == input.Target) return true;
        return false;
    }

    [Theory]
    [InlineData(new[] { 2, 7, 11, 15 }, 9, 0, 1)]
    [InlineData(new[] { 3, 2, 4 }, 6, 1, 2)]
    [InlineData(new[] { 3, 3 }, 6, 0, 1)]
    [InlineData(new[] { -3, 4, 3, 90 }, 0, 0, 2)]
    public void Both_approaches_solve_the_examples(int[] nums, int target, int a, int b)
    {
        foreach (var approach in P.Approaches)
        {
            var run = approach.Run(new(nums, target));
            Assert.Equal([a, b], run.Result.Order());
        }
    }

    [Fact]
    public void Random_inputs_including_duplicates_negatives_and_no_solution()
    {
        var rng = new Random(99);
        for (int trial = 0; trial < 300; trial++)
        {
            var input = P.RandomInput(rng);
            bool expected = HasPair(input);
            foreach (var approach in P.Approaches)
            {
                var run = approach.Run(input);
                Assert.Equal(expected, run.Result.Length == 2);
                if (expected) Assert.True(IsValid(input, run.Result), $"{approach.Key} gave an invalid pair");
                Assert.Equal(run.Steps[^1].Counters.Sum(c => long.Parse(c.Value)), run.Operations);
            }
        }
    }

    [Fact]
    public void Worst_case_counts_match_theory()
    {
        var brute = P.Approaches.Single(a => a.Key == "brute");
        var map = P.Approaches.Single(a => a.Key == "hashmap");
        foreach (int n in new[] { 10, 100, 1000 })
        {
            var input = P.WorstCase(n);
            Assert.Equal((long)n * (n - 1) / 2, brute.Measure(input).Counters["comparisons"]);
            Assert.Equal(n, map.Measure(input).Counters["lookups"]);
            Assert.Equal(n, map.Measure(input).Counters["insertions"]);
        }
    }

    [Fact]
    public void Hash_map_steps_show_the_dictionary_and_lookups()
    {
        var run = P.Approaches.Single(a => a.Key == "hashmap").Run(new([2, 7, 11, 15], 9));
        var lookup = run.Steps.First(s => s.Label == "lookup");
        Assert.Equal("7", lookup.Map!.ProbeKey);
        Assert.False(lookup.Map.ProbeHit);
        var found = run.Steps.Single(s => s.Label == "found");
        Assert.True(found.Map!.ProbeHit);
        Assert.Contains(found.Map.Entries, e => e.Key == "2" && e.Value == "0");
        Assert.All(run.Steps.Where(s => s.Label != ""), s => Assert.True(CodeLibrary.Get("two-sum-hashmap").LineOf(s.Label) > 0));
        var brute = P.Approaches.Single(a => a.Key == "brute").Run(new([2, 7, 11, 15], 9));
        Assert.All(brute.Steps.Where(s => s.Label != ""), s => Assert.True(CodeLibrary.Get("two-sum-brute").LineOf(s.Label) > 0));
    }

    [Theory]
    [InlineData("1", "2")]
    [InlineData("1, x", "2")]
    [InlineData("1, 2", "abc")]
    [InlineData("1, 200", "2")]
    public void Parse_rejects_bad_input(string numbers, string target)
    {
        Assert.Throws<FormatException>(() => ProblemDef.Parse(numbers, target));
    }

    [Fact]
    public void Parse_accepts_brackets_and_negatives()
    {
        var input = ProblemDef.Parse("[-3, 4, 3, 90]", " 0 ");
        Assert.Equal([-3, 4, 3, 90], input.Numbers);
        Assert.Equal(0, input.Target);
    }
}
