using System.Text.Json;
using System.Text.Json.Nodes;
using AlgoSharp.Playground.Compilation;
using Microsoft.CodeAnalysis;

namespace AlgoSharp.Playground;

/// <summary>How a parameter is typed in the editor and passed to the user's method.</summary>
/// <summary>How a parameter is typed in the editor. <see cref="Json"/> = any JSON value, checked by the runner against the C# type.</summary>
public enum ArgKind { IntArray, Int, UInt, String, IntMatrix, LinkedList, Tree, Json }

public sealed record ParamSpec(string Name, ArgKind Kind, string Placeholder);

/// <summary>A LeetCode-style example: argument texts, the expected output as shown, and why.</summary>
public sealed record ProblemExample(string[] Args, string Output, string? Explanation = null);

/// <summary>One test case: raw argument texts in parameter order.</summary>
public sealed record PlaygroundCase(string Label, IReadOnlyList<string> Args);

/// <summary>Verdict for one test case, decided by the problem's checker.</summary>
/// <summary>Verdict for one test case. <see cref="Graded"/> is false when there is no verified expected output.</summary>
public sealed record Verdict(bool Passed, string Explanation, string? Expected, bool Graded = true);

/// <summary>
/// A problem users solve with their own code. Everything problem-specific is data: the page,
/// compiler, sandbox and visualizer are shared by all problems.
/// </summary>
public sealed record PlaygroundProblem(
    string Slug,
    string Title,
    int Number,
    string Category,
    string Description,
    IReadOnlyList<string> Constraints,
    IReadOnlyList<ParamSpec> Params,
    IReadOnlyList<ProblemExample> Examples,
    IReadOnlyList<string[]> ExtraTests,
    EntrySignature Entry,
    string StarterCode,
    IReadOnlyList<string> Hints,
    Func<JsonElement[], JsonNode?> Reference)
{
    public string Difficulty { get; init; } = "Easy";

    /// <summary>Optional extra input rule (e.g. "nums must be sorted"); returns an error message or null.</summary>
    public Func<JsonElement[], string?>? Validate { get; init; }

    /// <summary>Optional custom checker when several answers are valid (e.g. Two Sum).</summary>
    public Func<JsonElement[], string?, Verdict>? Checker { get; init; }

    /// <summary>Optional random input generator (argument texts).</summary>
    public Func<Random, string[]>? RandomCase { get; init; }

    /// <summary>Maps parsed arguments to what the runner receives (default: unchanged).</summary>
    public Func<JsonElement[], IReadOnlyList<JsonElement>>? ToRunnerArgs { get; init; }

    public IReadOnlyList<PlaygroundCase> DefaultCases =>
    [
        .. Examples.Select((e, i) => new PlaygroundCase($"Example {i + 1}", e.Args)),
        .. ExtraTests.Select((t, i) => new PlaygroundCase($"Test {i + 1}", t)),
    ];

    public string Describe(IReadOnlyList<string> args) =>
        string.Join(", ", Params.Select((p, i) => $"{p.Name} = {(i < args.Count ? args[i] : "?")}"));

    /// <summary>Parses and validates argument texts. Throws <see cref="FormatException"/> with a readable message.</summary>
    public JsonElement[] Parse(IReadOnlyList<string> args)
    {
        if (args.Count != Params.Count) throw new FormatException($"Expected {Params.Count} argument(s).");
        var parsed = Params.Select((p, i) => ProblemInputs.Parse(p, args[i])).ToArray();
        if (Validate?.Invoke(parsed) is { } error) throw new FormatException(error);
        return parsed;
    }

    public IReadOnlyList<JsonElement> RunnerArgs(JsonElement[] parsed) => ToRunnerArgs?.Invoke(parsed) ?? parsed;

    public Verdict Check(JsonElement[] parsed, string? resultJson)
    {
        if (Checker is not null) return Checker(parsed, resultJson);
        var expected = Reference(parsed);
        if (expected is null) return new(false, "No expected output is defined for this input, so it ran without grading.", null, Graded: false);
        string expectedText = expected.ToJsonString();
        JsonNode? actual;
        try
        {
            actual = resultJson is null ? null : JsonNode.Parse(resultJson);
        }
        catch (JsonException)
        {
            return new(false, "The result couldn't be read.", expectedText);
        }
        if (actual is JsonObject { } o && o.ContainsKey("cycle"))
        {
            return new(false, "The returned list contains a cycle.", expectedText);
        }
        bool ok = JsonNode.DeepEquals(Normalize(actual), Normalize(expected));
        return new(ok, ok ? $"Returned {ProblemInputs.Show(actual)}, as expected." : $"Returned {ProblemInputs.Show(actual)}, expected {ProblemInputs.Show(expected)}.", expectedText);
    }

    private static JsonNode? Normalize(JsonNode? node) => node is null ? null : JsonNode.Parse(node.ToJsonString());
}

/// <summary>Parsing and display of argument texts.</summary>
public static class ProblemInputs
{
    public const int MaxArray = 20, MaxString = 40, MaxTreeNodes = 31;

    public static JsonElement Parse(ParamSpec p, string raw)
    {
        string text = raw.Trim();
        if (p.Kind == ArgKind.String && !text.StartsWith('"')) text = JsonSerializer.Serialize(text);
        JsonElement json;
        try
        {
            json = JsonDocument.Parse(text.Length == 0 ? "null" : text).RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new FormatException($"{p.Name}: couldn't read “{raw}”. Expected {Expectation(p.Kind)}.");
        }
        string? error = p.Kind switch
        {
            ArgKind.Int => json.ValueKind == JsonValueKind.Number && json.TryGetInt32(out _) ? null : "a whole number",
            ArgKind.UInt => json.ValueKind == JsonValueKind.Number && json.TryGetUInt32(out _) ? null : "a whole number from 0 to 4294967295",
            ArgKind.String => json.ValueKind == JsonValueKind.String && json.GetString()!.Length <= MaxString ? null : $"text of at most {MaxString} characters",
            ArgKind.IntArray or ArgKind.LinkedList => IsIntArray(json) && json.GetArrayLength() <= MaxArray ? null : $"an array of at most {MaxArray} whole numbers, like [1, 2, 3]",
            ArgKind.IntMatrix => json.ValueKind == JsonValueKind.Array && json.GetArrayLength() <= MaxArray && json.EnumerateArray().All(r => IsIntArray(r) && r.GetArrayLength() == 2)
                ? null : "a list of [start, end] pairs, like [[0, 30], [5, 10]]",
            ArgKind.Tree => json.ValueKind == JsonValueKind.Array && json.GetArrayLength() <= 63
                            && json.EnumerateArray().All(e => e.ValueKind == JsonValueKind.Null || e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out _))
                            && TreeSize(json) <= MaxTreeNodes
                ? null : $"a level-order array with nulls (at most {MaxTreeNodes} nodes), like [3, 9, 20, null, null, 15, 7]",
            ArgKind.Json => text.Length <= 4000 ? null : "a value of at most 4000 characters",
            _ => null,
        };
        if (error is not null) throw new FormatException($"{p.Name}: expected {error}.");
        return json;
    }

    private static int TreeSize(JsonElement json) => json.EnumerateArray().Count(e => e.ValueKind != JsonValueKind.Null);

    private static bool IsIntArray(JsonElement json) =>
        json.ValueKind == JsonValueKind.Array && json.EnumerateArray().All(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out _));

    private static string Expectation(ArgKind kind) => kind switch
    {
        ArgKind.Int or ArgKind.UInt => "a number",
        ArgKind.String => "text",
        ArgKind.IntMatrix => "[[start, end], ...]",
        ArgKind.Tree => "a level-order array like [1, null, 2]",
        _ => "an array like [1, 2, 3]",
    };

    public static string Show(JsonNode? node) => node switch
    {
        null => "null",
        _ => node.ToJsonString().Replace(",", ", "),
    };

    public static int[] Ints(JsonElement e) => [.. e.EnumerateArray().Select(x => x.GetInt32())];

    public static int?[] NullableInts(JsonElement e) => [.. e.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Null ? (int?)null : x.GetInt32())];

    /// <summary>A size hint for the assistant: array/string/tree length, or the number itself.</summary>
    public static int SizeOf(JsonElement[] args)
    {
        foreach (var a in args)
        {
            if (a.ValueKind == JsonValueKind.Array) return a.GetArrayLength();
            if (a.ValueKind == JsonValueKind.String) return a.GetString()!.Length;
        }
        return args.Length > 0 && args[0].ValueKind == JsonValueKind.Number && args[0].TryGetInt32(out int n) ? Math.Max(0, n) : 0;
    }
}

internal static class Sig
{
    public static bool IntArray(ITypeSymbol t) => t is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Int32 };
    public static bool Int(ITypeSymbol t) => t.SpecialType == SpecialType.System_Int32;
    public static bool UInt(ITypeSymbol t) => t.SpecialType == SpecialType.System_UInt32;
    public static bool String(ITypeSymbol t) => t.SpecialType == SpecialType.System_String;
    public static bool IntMatrix(ITypeSymbol t) => t is IArrayTypeSymbol { Rank: 1, ElementType: IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Int32 } };
    public static bool ListNode(ITypeSymbol t) => t.Name == "ListNode";
    public static bool TreeNode(ITypeSymbol t) => t.Name == "TreeNode";
}
