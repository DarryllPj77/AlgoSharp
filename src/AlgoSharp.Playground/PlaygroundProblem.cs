using System.Text.Json;
using AlgoSharp.Core.Problems;
using AlgoSharp.Playground.Compilation;
using Microsoft.CodeAnalysis;

namespace AlgoSharp.Playground;

public sealed record PlaygroundCase(string Label, ProblemInput Input);

/// <summary>Verdict for one test case, decided by the problem's checker (not by string equality).</summary>
public sealed record Verdict(bool Passed, string Explanation);

/// <summary>A problem users can solve with their own code.</summary>
public sealed record PlaygroundProblem(
    ProblemDef Problem,
    EntrySignature Entry,
    string StarterCode,
    IReadOnlyList<string> Hints,
    Func<ProblemInput, IReadOnlyList<JsonElement>> ToArgs,
    Func<ProblemInput, string?, Verdict> Check)
{
    public string Slug => Problem.Slug;

    public IReadOnlyList<PlaygroundCase> DefaultCases =>
        Problem.Examples.Select((e, i) => new PlaygroundCase($"Example {i + 1}", ParseExample(e.Input))).ToList();

    private static ProblemInput ParseExample(string input)
    {
        int open = input.IndexOf('['), close = input.IndexOf(']'), eq = input.LastIndexOf('=');
        return ProblemDef.Parse(input[(open + 1)..close], input[(eq + 1)..]);
    }
}

public static class PlaygroundCatalog
{
    private static bool IsIntArray(ITypeSymbol t) => t is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Int32 };

    private static bool IsInt(ITypeSymbol t) => t.SpecialType == SpecialType.System_Int32;

    public static readonly PlaygroundProblem TwoSum = new(
        ProblemCatalog.TwoSumProblem,
        new EntrySignature("TwoSum", [IsIntArray, IsInt], "int[] TwoSum(int[] nums, int target)"),
        """
        public class Solution
        {
            public int[] TwoSum(int[] nums, int target)
            {
                // Write your solution here.
                // Return the two indices, or an empty array if no pair adds up to target.
                for (int i = 0; i < nums.Length; i++)
                {
                    for (int j = i + 1; j < nums.Length; j++)
                    {
                        if (nums[i] + nums[j] == target)
                        {
                            return new int[] { i, j };
                        }
                    }
                }
                return new int[0];
            }
        }
        """,
        [
            "Start simple: can you check every pair of positions (i, j)? Make sure you never pair an element with itself.",
            "Once the pairs version passes, count how many pairs it checks for n numbers. Where does the time go?",
            "For a fixed nums[i], you're searching for exactly one value: target − nums[i]. Could you remember values you've already seen?",
            "A Dictionary<int, int> can map a value to its index. Check it before inserting the current number, so an element is never paired with itself.",
        ],
        input =>
        [
            JsonSerializer.SerializeToElement(input.Numbers),
            JsonSerializer.SerializeToElement(input.Target),
        ],
        CheckTwoSum);

    public static IReadOnlyList<PlaygroundProblem> All { get; } = [TwoSum];

    public static PlaygroundProblem? Find(string slug) => All.FirstOrDefault(p => string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>Any valid pair is accepted (there can be several); [] is correct only when no pair exists.</summary>
    private static Verdict CheckTwoSum(ProblemInput input, string? resultJson)
    {
        int[]? result;
        try
        {
            result = resultJson is null ? null : JsonSerializer.Deserialize<int[]>(resultJson);
        }
        catch (JsonException)
        {
            return new(false, "The result isn't an int[].");
        }
        var nums = input.Numbers;
        bool exists = false;
        for (int i = 0; i < nums.Length && !exists; i++)
            for (int j = i + 1; j < nums.Length && !exists; j++)
                exists = nums[i] + nums[j] == input.Target;

        if (result is null) return new(false, "Returned null. Return an empty array when there is no pair.");
        if (result.Length == 0)
            return exists ? new(false, "Returned [], but a pair does exist.") : new(true, "Correctly returned [] — no pair adds up to the target.");
        if (result.Length != 2) return new(false, $"Expected two indices, got {result.Length}.");
        int a = result[0], b = result[1];
        if (a < 0 || b < 0 || a >= nums.Length || b >= nums.Length) return new(false, $"Index out of range in [{a}, {b}].");
        if (a == b) return new(false, $"Used index {a} twice; the two elements must be different.");
        return nums[a] + nums[b] == input.Target
            ? new(true, $"nums[{a}] + nums[{b}] = {nums[a]} + {nums[b]} = {input.Target}.")
            : new(false, $"nums[{a}] + nums[{b}] = {nums[a] + nums[b]}, not {input.Target}.");
    }
}
