using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AlgoSharp.Playground.Library;

public sealed record SchemaField(string Name, string Type, bool Required, string Description);

/// <summary>
/// The AlgoSharp problem-import format. The validator (<see cref="ProblemImporter"/>) and the
/// copyable AI prompt are both generated from these definitions, so they always agree.
/// </summary>
public static class ImportSchema
{
    public const string Format = "algosharp/problems";
    public const int Version = 1;

    public static readonly IReadOnlyList<SchemaField> Document =
    [
        new("format", "string", true, $"Always \"{Format}\"."),
        new("version", "integer", true, $"Schema version. Use {Version}."),
        new("collection", "string", false, "Lesson or collection name the problems belong to."),
        new("problems", "array of problem", true, "One entry per exercise, in the original order."),
    ];

    public static readonly IReadOnlyList<SchemaField> Problem =
    [
        new("title", "string", true, "Exercise title without the level prefix, e.g. \"Count Local Maximums\"."),
        new("level", "integer", false, "Level / exercise number from the source, e.g. 14."),
        new("difficulty", "string", false, "\"Easy\", \"Medium\" or \"Hard\". Omit if the source doesn't say."),
        new("category", "string", false, "Main topic, e.g. \"Arrays\"."),
        new("concepts", "array of string", false, "Concepts practiced, e.g. [\"Arrays\", \"Loops\"]."),
        new("description", "string", true, "The full problem statement, preserved from the source."),
        new("input", "string", false, "Description of the input."),
        new("output", "string", false, "Description of the output."),
        new("examples", "array of example", false, "Examples exactly as given in the source."),
        new("constraints", "array of string", false, "Constraints from the source."),
        new("instructions", "string", false, "Any additional instructions from the source."),
        new("signature", "string", false, "C# method signature, e.g. \"public int CountLocalMaximums(int[] nums)\". Parameter types: " + string.Join(", ", SignatureParser.SupportedTypes) + "."),
        new("starterCode", "string", false, "C# starter code: a public class containing the method with an empty body that compiles (e.g. return 0;). No solution."),
        new("tests", "array of test", false, "Extra test cases. Set verified to false unless the expected output was checked."),
    ];

    public static readonly IReadOnlyList<SchemaField> Example =
    [
        new("input", "string", true, "Input as written in the source, e.g. \"[2, 7, 3, 9, 4, 8, 1]\" or \"nums = [2, 7], target = 9\"."),
        new("output", "string", true, "Expected output as written, e.g. \"3\"."),
        new("explanation", "string", false, "Explanation, if the source gives one."),
        new("args", "array of JSON values", false, "One JSON value per signature parameter, e.g. [[2, 7, 3, 9, 4, 8, 1]]."),
    ];

    public static readonly IReadOnlyList<SchemaField> Test =
    [
        new("args", "array of JSON values", true, "One JSON value per signature parameter, in order."),
        new("expected", "JSON value", true, "Expected return value as JSON, e.g. 3, true, [1, 2] or \"text\"."),
        new("verified", "boolean", false, "true only if the expected value was actually checked; otherwise false."),
    ];

    /// <summary>A complete, valid example document (also used by "View format example").</summary>
    public static string ExampleJson()
    {
        var doc = new JsonObject
        {
            ["format"] = Format,
            ["version"] = Version,
            ["collection"] = "Lesson 4: Neighbor Comparisons",
            ["problems"] = new JsonArray(
                new JsonObject
                {
                    ["title"] = "Count Local Maximums",
                    ["level"] = 14,
                    ["difficulty"] = "Easy",
                    ["category"] = "Arrays",
                    ["concepts"] = new JsonArray("Arrays", "Loops", "Multiple Conditions", "Neighbor Comparison"),
                    ["description"] = "Given an array of integers, count the elements that are strictly greater than both of their neighbors. The first and last elements are never local maximums.",
                    ["input"] = "An integer array nums.",
                    ["output"] = "The number of local maximums.",
                    ["examples"] = new JsonArray(new JsonObject
                    {
                        ["input"] = "[2, 7, 3, 9, 4, 8, 1]",
                        ["output"] = "3",
                        ["explanation"] = "7, 9 and 8 are each greater than both neighbors.",
                        ["args"] = new JsonArray(new JsonArray(2, 7, 3, 9, 4, 8, 1)),
                    }),
                    ["constraints"] = new JsonArray("1 <= nums.Length <= 100"),
                    ["signature"] = "public int CountLocalMaximums(int[] nums)",
                    ["starterCode"] = "public class Solution\n{\n    public int CountLocalMaximums(int[] nums)\n    {\n        return 0;\n    }\n}",
                    ["tests"] = new JsonArray(new JsonObject
                    {
                        ["args"] = new JsonArray(new JsonArray(1, 2, 3)),
                        ["expected"] = 0,
                        ["verified"] = false,
                    }),
                }),
        };
        return doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>The copyable prompt for ChatGPT, Claude, etc., generated from the schema above.</summary>
    public static string AiPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Convert the programming lesson or exercises I paste below into AlgoSharp-compatible JSON.");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- Return ONLY valid JSON: no Markdown code fences, no comments, no text before or after it.");
        sb.AppendLine("- Create one entry in \"problems\" for each exercise (e.g. Level 12, Level 13 and Level 14 become three entries).");
        sb.AppendLine("- Preserve the original requirements, wording, examples and constraints. Do not invent missing details; omit optional fields you don't know.");
        sb.AppendLine("- Do not provide solutions, hints that reveal the solution, or pseudocode. starterCode must have an empty method body that still compiles (e.g. return 0;).");
        sb.AppendLine("- Do not invent verified test cases: set \"verified\": false unless the expected value is stated in the source or was actually computed.");
        sb.AppendLine($"- Supported C# parameter types: {string.Join(", ", SignatureParser.SupportedTypes)}.");
        sb.AppendLine();
        sb.AppendLine($"Schema (format \"{Format}\", version {Version}):");
        Describe(sb, "Document", Document);
        Describe(sb, "problem", Problem);
        Describe(sb, "example", Example);
        Describe(sb, "test", Test);
        sb.AppendLine();
        sb.AppendLine("Complete valid example:");
        sb.AppendLine(ExampleJson());
        sb.AppendLine();
        sb.AppendLine("Lesson / exercises to convert:");
        sb.AppendLine("<paste your lesson here>");
        return sb.ToString();
    }

    private static void Describe(StringBuilder sb, string name, IReadOnlyList<SchemaField> fields)
    {
        sb.AppendLine($"{name}:");
        foreach (var f in fields)
        {
            sb.AppendLine($"  - {f.Name} ({f.Type}{(f.Required ? ", required" : ", optional")}): {f.Description}");
        }
    }
}
