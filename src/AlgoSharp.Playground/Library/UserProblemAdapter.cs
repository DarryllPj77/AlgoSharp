using System.Text.Json;
using System.Text.Json.Nodes;
using AlgoSharp.Playground.Compilation;

namespace AlgoSharp.Playground.Library;

/// <summary>How a personal problem will run in Solve It, and why.</summary>
public sealed record SolveSetup(PlaygroundProblem Problem, ProblemMode EffectiveMode, bool HasContract, string Explanation);

/// <summary>
/// Turns a personal problem into a Solve It problem, reusing the shared compiler, sandbox and
/// visualizer. Grading only happens in Challenge mode with a valid signature and verified tests;
/// everything else runs ungraded, with the source's expected outputs shown as unverified.
/// </summary>
public static class UserProblemAdapter
{
    /// <summary>Why a problem can or can't be graded automatically.</summary>
    public static (bool Ok, string Reason) CanChallenge(UserProblem p)
    {
        if (!SignatureParser.TryParse(p.Signature, out var sig, out var error)) return (false, $"Signature: {error}");
        var verified = p.Tests.Where(t => t.Verified).ToList();
        if (verified.Count == 0) return (false, "No verified test cases yet. Verify at least one test (check its expected output) to enable grading.");
        for (int i = 0; i < verified.Count; i++)
        {
            if (Problems(sig!, verified[i]) is { } bad) return (false, $"Verified test {i + 1}: {bad}");
        }
        return (true, $"{verified.Count} verified test(s) against {sig!.Display}.");
    }

    private static string? Problems(ParsedSignature sig, UserTest t)
    {
        if (t.Args.Count != sig.Params.Count) return $"has {t.Args.Count} argument(s); the signature has {sig.Params.Count}.";
        for (int i = 0; i < t.Args.Count; i++)
        {
            try
            {
                ProblemInputs.Parse(new ParamSpec(sig.Params[i].Name, sig.Params[i].Kind, ""), t.Args[i]);
            }
            catch (FormatException ex)
            {
                return ex.Message;
            }
        }
        try
        {
            JsonNode.Parse(t.Expected);
        }
        catch (JsonException)
        {
            return "expected output isn't valid JSON (put text in quotes, e.g. \"yes\").";
        }
        return null;
    }

    public static SolveSetup ToPlayground(UserProblem p)
    {
        string slug = "mine-" + p.Id;
        if (!SignatureParser.TryParse(p.Signature, out var sig, out var sigError))
        {
            return new(Practice(p, slug), ProblemMode.Practice, false,
                p.Signature is null
                    ? "Practice mode: no method signature is defined, so write a static Main() and print your results. Nothing is graded."
                    : $"Practice mode: the signature couldn't be read ({sigError}). Write a static Main() instead, or fix the signature in Edit.");
        }

        var usable = p.Tests.Where(t => Problems(sig!, t) is null).ToList();
        var (challengeOk, reason) = CanChallenge(p);
        var mode = p.Mode == ProblemMode.Challenge && challengeOk ? ProblemMode.Challenge : ProblemMode.Practice;
        string explanation = mode == ProblemMode.Challenge
            ? $"Challenge mode: graded against {usable.Count(t => t.Verified)} verified test(s)."
            : p.Mode == ProblemMode.Challenge
                ? $"Practice mode (grading unavailable): {reason}"
                : "Practice mode: your method runs on the examples. Expected outputs from the source are shown but not verified, so nothing is graded.";

        var expectedByArgs = new Dictionary<string, UserTest>();
        foreach (var t in usable) expectedByArgs.TryAdd(Key(t.Args), t);

        var ps = sig!.Params.Select((x, i) => new ParamSpec(x.Name, x.Kind, usable.FirstOrDefault()?.Args[i] ?? "")).ToList();
        var entry = new EntrySignature(sig.Name, [.. sig.Params.Select(x => (ITypeSymbolMatcher)(t => SignatureParser.Matches(t, x.Type)))], sig.Display);

        Verdict Check(JsonElement[] args, string? resultJson)
        {
            if (!expectedByArgs.TryGetValue(Key(args.Select(a => a.GetRawText())), out var test))
                return new(false, "No expected output is defined for this input, so it ran without grading.", null, Graded: false);
            bool graded = mode == ProblemMode.Challenge && test.Verified;
            if (resultJson is null) return new(false, "The method didn't return a value.", test.Expected, graded);
            bool same = Equivalent(SafeParse(resultJson), SafeParse(test.Expected));
            string shown = ProblemInputs.Show(SafeParse(resultJson));
            return graded
                ? new(same, same ? $"Returned {shown}, as expected." : $"Returned {shown}, expected {ProblemInputs.Show(SafeParse(test.Expected))}.", test.Expected)
                : new(same, same ? $"Returned {shown}, which matches the source's expected output (not verified, so not graded)." : $"Returned {shown}; the source says {ProblemInputs.Show(SafeParse(test.Expected))} (not verified, so not graded).", test.Expected, Graded: false);
        }

        var problem = new PlaygroundProblem(
            slug, p.Title, p.Level ?? 0, p.Category ?? "My problems", Describe(p), p.Constraints, ps, [],
            [.. usable.Select(t => t.Args.ToArray())],
            entry,
            string.IsNullOrWhiteSpace(p.StarterCode) ? Starter(sig) : p.StarterCode!,
            [],
            a => expectedByArgs.TryGetValue(Key(a.Select(x => x.GetRawText())), out var t) ? SafeParse(t.Expected) : null)
        {
            Difficulty = p.Difficulty ?? "Unrated",
            Checker = Check,
        };
        return new(problem, mode, true, explanation);
    }

    /// <summary>Practice without a contract: the user writes static void Main() and prints results.</summary>
    private static PlaygroundProblem Practice(UserProblem p, string slug)
    {
        var lines = new List<string> { "public class Program", "{", "    public static void Main()", "    {" };
        foreach (var e in p.Examples.Take(3))
        {
            lines.Add($"        // Example input:  {e.Input.Replace("\n", " ")}");
            lines.Add($"        // Expected output: {e.Output.Replace("\n", " ")}");
        }
        lines.AddRange(["        // Write your solution here and print the result, e.g. Console.WriteLine(answer);", "    }", "}"]);
        return new PlaygroundProblem(
            slug, p.Title, p.Level ?? 0, p.Category ?? "My problems", Describe(p), p.Constraints, [], [],
            [Array.Empty<string>()],
            new EntrySignature("Main", [], "static void Main()"),
            string.IsNullOrWhiteSpace(p.StarterCode) ? string.Join("\n", lines) : p.StarterCode!,
            [],
            _ => null)
        {
            Difficulty = p.Difficulty ?? "Unrated",
            Checker = (_, _) => new(false, "Ran your program. Practice mode has no automatic grading; compare the console output with the examples.", null, Graded: false),
        };
    }

    public static string Starter(ParsedSignature sig) => $$"""
        public class Solution
        {
            public {{sig.Display}}
            {
                // Write your solution here.
                return default;
            }
        }
        """;

    private static string Describe(UserProblem p) => p.Description;

    private static string Key(IEnumerable<string> args) => string.Join("\u0001", args.Select(a => SafeParse(a)?.ToJsonString() ?? a.Trim()));

    private static JsonNode? SafeParse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return JsonValue.Create(json);
        }
    }

    /// <summary>Deep JSON equality; numbers compare numerically (3 == 3.0, small double tolerance).</summary>
    public static bool Equivalent(JsonNode? a, JsonNode? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a is JsonValue va && b is JsonValue vb && va.TryGetValue(out double da) && vb.TryGetValue(out double db))
            return Math.Abs(da - db) <= 1e-6 * Math.Max(1, Math.Max(Math.Abs(da), Math.Abs(db)));
        if (a is JsonArray xa && b is JsonArray xb) return xa.Count == xb.Count && xa.Zip(xb).All(z => Equivalent(z.First, z.Second));
        return JsonNode.DeepEquals(a, b);
    }
}
