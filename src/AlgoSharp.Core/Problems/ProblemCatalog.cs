using AlgoSharp.Core.Algorithms;
using AlgoSharp.Core.Catalog;
using AlgoSharp.Core.Engine;

namespace AlgoSharp.Core.Problems;

/// <summary>The input shared by every approach to a problem.</summary>
public sealed record ProblemInput(int[] Numbers, int Target);

public sealed record ProblemExample(string Input, string Output, string Explanation);

/// <summary>Operation counts from a counting-only run on a larger input (no steps recorded).</summary>
public sealed record ScalePoint(int N, IReadOnlyDictionary<string, long> Counters)
{
    public long Total => Counters.Values.Sum();
}

/// <summary>One way of solving a problem: real C# code, its complexity, and a runner.</summary>
public sealed record ApproachDef(
    string Key,
    string Name,
    string Technique,
    string CodeId,
    string Idea,
    Complexity Complexity,
    IReadOnlyList<string> Tradeoffs,
    Func<int[], int, ArrayTracer, int[]> Solve)
{
    /// <summary>Runs the approach on the input and records every step.</summary>
    public ApproachRun Run(ProblemInput input)
    {
        var tracer = new ArrayTracer(ArrayLayout.Cells, input.Numbers.Length, input.Numbers);
        int[] result = Solve((int[])input.Numbers.Clone(), input.Target, tracer);
        return new ApproachRun(this, tracer.Steps, result, tracer.Counters.ToDictionary());
    }

    /// <summary>Runs the same code without recording steps, just to count operations.</summary>
    public ScalePoint Measure(ProblemInput input)
    {
        var tracer = new ArrayTracer(ArrayLayout.Cells, 0) { Recording = false };
        Solve(input.Numbers, input.Target, tracer);
        return new ScalePoint(input.Numbers.Length, tracer.Counters.ToDictionary());
    }
}

public sealed record ApproachRun(ApproachDef Approach, IReadOnlyList<Step> Steps, int[] Result, IReadOnlyDictionary<string, long> Counters)
{
    public long Operations => Counters.Values.Sum();
}

public sealed record ProblemDef(
    string Slug,
    string Title,
    string Number,
    Difficulty Difficulty,
    IReadOnlyList<string> Techniques,
    string Statement,
    IReadOnlyList<ProblemExample> Examples,
    IReadOnlyList<string> Constraints,
    IReadOnlyList<ApproachDef> Approaches,
    ProblemInput DefaultInput,
    Func<Random, ProblemInput> RandomInput,
    Func<int, ProblemInput> WorstCase,
    string TargetLabel = "Target")
{
    public bool Available => Approaches.Count > 0;

    public const int MinCount = 2, MaxCount = 12, MinValue = -99, MaxValue = 99;

    /// <summary>Parses user input; throws <see cref="FormatException"/> with a readable message.</summary>
    public static ProblemInput Parse(string numbers, string target)
    {
        var values = new List<int>();
        foreach (var part in numbers.Split([',', ' ', ';', '[', ']'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, out int v)) throw new FormatException($"“{part}” is not a whole number.");
            if (v is < MinValue or > MaxValue) throw new FormatException($"{v} is outside {MinValue}–{MaxValue}.");
            values.Add(v);
        }
        if (values.Count < MinCount) throw new FormatException($"Enter at least {MinCount} numbers.");
        if (values.Count > MaxCount) throw new FormatException($"At most {MaxCount} numbers, please.");
        if (!int.TryParse(target.Trim(), out int t) || t is < -999 or > 999) throw new FormatException("The target must be a whole number between −999 and 999.");
        return new ProblemInput([.. values], t);
    }
}

public static class ProblemCatalog
{
    public static readonly ProblemDef TwoSumProblem = new(
        "two-sum", "Two Sum", "1", Difficulty.Beginner,
        ["Hash Map", "Arrays"],
        "Given an array of integers nums and an integer target, return the indices of the two numbers that add up to target. Each input is assumed to have at most one solution, and you may not use the same element twice. Return an empty array if no pair exists.",
        [
            new("nums = [2, 7, 11, 15], target = 9", "[0, 1]", "nums[0] + nums[1] = 2 + 7 = 9."),
            new("nums = [3, 2, 4], target = 6", "[1, 2]", "nums[1] + nums[2] = 2 + 4 = 6. Using 3 twice is not allowed."),
            new("nums = [3, 3], target = 6", "[0, 1]", "Duplicate values are fine as long as the indices differ."),
        ],
        ["2 ≤ nums.Length ≤ 12 here (10⁴ on LeetCode)", "−99 ≤ nums[i] ≤ 99, negatives allowed", "Returns [] when there is no solution"],
        [
            new("brute", "Brute Force", "Nested loops", "two-sum-brute",
                "Check every pair of indices. Simple and needs no extra memory, but the number of pairs grows quadratically.",
                new("O(1)", "O(n²)", "O(n²)", "O(1)", "Best case: the first pair matches. Worst case: no pair, all n(n−1)/2 pairs checked."),
                ["No extra memory.", "Easy to get right; a good first answer in an interview.", "Too slow for large n: 10⁴ elements ≈ 5·10⁷ pairs."],
                TwoSum.BruteForce),
            new("hashmap", "Hash Map", "Dictionary<int, int>", "two-sum-hashmap",
                "Remember every value seen so far with its index. For each number, look up target − number in O(1) instead of scanning again.",
                new("O(1)", "O(n)", "O(n)", "O(n)", "Average-case O(1) dictionary operations; worst case for a hash table is O(n) per operation with many collisions."),
                ["One pass: each element is looked up once and inserted once.", "Trades O(n) extra memory for speed.", "Handles duplicates by checking before inserting."],
                TwoSum.HashMap),
        ],
        new([2, 7, 11, 15], 9),
        RandomTwoSum,
        n => new(Enumerable.Range(0, n).Select(i => 2 * i).ToArray(), -1));

    public static IReadOnlyList<ProblemDef> All { get; } =
    [
        TwoSumProblem,
        Soon("valid-palindrome", "Valid Palindrome", "125", ["Two Pointers"]),
        Soon("max-sum-subarray-k", "Maximum Sum Subarray of Size K", "—", ["Sliding Window"]),
        Soon("two-sum-ii", "Two Sum II (Sorted Array)", "167", ["Two Pointers"]),
        Soon("contains-duplicate", "Contains Duplicate", "217", ["Hash Set"]),
        Soon("binary-search-problem", "Binary Search", "704", ["Binary Search"]),
    ];

    public static ProblemDef? Find(string slug) =>
        All.FirstOrDefault(p => string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>Random input: usually with a solution, sometimes without; may contain duplicates and negatives.</summary>
    private static ProblemInput RandomTwoSum(Random rng)
    {
        int n = rng.Next(5, 10);
        int[] nums = Enumerable.Range(0, n).Select(_ => rng.Next(-15, 41)).ToArray();
        if (rng.Next(4) == 0)
        {
            // Pick a target no pair can reach.
            var sums = new HashSet<int>();
            for (int i = 0; i < n; i++) for (int j = i + 1; j < n; j++) sums.Add(nums[i] + nums[j]);
            int t = rng.Next(-20, 80);
            while (sums.Contains(t)) t++;
            return new(nums, t);
        }
        int a = rng.Next(n), b = rng.Next(n - 1);
        if (b >= a) b++;
        return new(nums, nums[a] + nums[b]);
    }

    private static ProblemDef Soon(string slug, string title, string number, string[] techniques) =>
        new(slug, title, number, Difficulty.Beginner, techniques, "", [], [], [], new([], 0), _ => new([], 0), _ => new([], 0));
}
