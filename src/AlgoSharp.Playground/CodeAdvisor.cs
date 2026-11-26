using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AlgoSharp.Playground;

/// <summary>What the assistant can say about a solution, with how sure it is.</summary>
public sealed record Advice(
    string Approach,
    string Explanation,
    string Time,
    string Space,
    string Confidence,
    IReadOnlyList<string> Observations,
    IReadOnlyList<string> Suggestions);

/// <summary>
/// Explains a solution from its structure (loops, collections, recursion) and its measured
/// trace. Estimates are labelled as estimates; when the structure is unclear it says so
/// instead of guessing.
/// </summary>
public static class CodeAdvisor
{
    public static Advice Analyze(SyntaxTree tree, string entryMethod, IReadOnlyList<(int N, int Conditions, bool Passed)> runs, int hintsAvailable)
    {
        var root = tree.GetRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault(m => m.Identifier.Text == entryMethod);
        if (method is null) return new("Unknown", "Couldn't find the solution method.", "—", "—", "none", [], []);

        var parameters = method.ParameterList.Parameters.Select(p => p.Identifier.Text).ToHashSet();
        var body = (SyntaxNode?)method.Body ?? method.ExpressionBody;
        var loops = body?.DescendantNodes().Where(IsLoop).ToList() ?? [];

        int depth = 0;
        bool allInputBound = true;
        foreach (var loop in loops)
        {
            int d = 1 + loop.Ancestors().TakeWhile(a => a != body).Count(IsLoop);
            depth = Math.Max(depth, d);
            if (!IsInputBound(loop, parameters)) allInputBound = false;
        }

        string text = body?.ToString() ?? "";
        // Fields and helper methods count too (e.g. a heap kept by a design class).
        string classText = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.ToString() ?? text;
        bool usesHeap = classText.Contains("PriorityQueue");
        bool usesHash = body?.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Any(o => o.Type.ToString().StartsWith("Dictionary") || o.Type.ToString().StartsWith("HashSet")) == true
                        || body?.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>().Any(o => o.Parent?.Parent?.Parent is VariableDeclarationSyntax v && (v.Type.ToString().StartsWith("Dictionary") || v.Type.ToString().StartsWith("HashSet"))) == true;
        bool usesSort = text.Contains("Array.Sort") || text.Contains(".Sort(") || text.Contains("OrderBy");
        bool recursive = body?.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(i => i.Expression is IdentifierNameSyntax id && id.Identifier.Text == entryMethod) == true;
        bool allocatesList = body?.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().Any() == true;

        string approach, explanation;
        if (usesHeap)
        {
            approach = "Heap (priority queue)";
            explanation = "You keep values in a PriorityQueue, which always gives you the smallest priority first. Enqueue and Dequeue cost O(log n) each.";
        }
        else if (recursive)
        {
            approach = "Recursion";
            explanation = "The method calls itself. Its cost depends on how many calls are made, which the trace below lets you count.";
        }
        else if (usesHash && depth <= 1)
        {
            approach = "Hash map lookup";
            explanation = "You keep a hash-based collection and look values up in it instead of scanning the array again, the classic one-pass idea.";
        }
        else if (usesSort)
        {
            approach = "Sorting first";
            explanation = "You sort the data and then process it. Remember that sorting moves elements, so original indices need care.";
        }
        else if (depth >= 2)
        {
            approach = "Brute force (nested loops)";
            explanation = "You compare elements pairwise with nested loops. This is the most direct approach and a great first solution.";
        }
        else if (depth == 1)
        {
            approach = "Single pass";
            explanation = "You walk the input once.";
        }
        else
        {
            approach = "No loops";
            explanation = "The method has no loops over the input.";
        }

        string time, space, confidence;
        if (usesHeap)
        {
            (time, confidence) = ("O(log n) per heap operation", "estimate: each Enqueue/Dequeue is logarithmic in the heap's size");
        }
        else if (recursive)
        {
            (time, confidence) = ("depends on the number of calls", "low: recursion isn't estimated from structure");
        }
        else if (!allInputBound && depth > 0)
        {
            (time, confidence) = ("not determined", "low: a loop's bounds don't come directly from the input size");
        }
        else
        {
            string loopsPart = depth switch { 0 => "O(1)", 1 => "O(n)", 2 => "O(n²)", _ => $"O(n^{depth})" };
            time = usesSort ? (depth >= 2 ? loopsPart : "O(n log n)") : loopsPart;
            if (usesHash) time += " average (hash lookups are O(1) on average)";
            confidence = "estimate from loop nesting";
        }
        bool classCollection = classText.Contains("new Dictionary") || classText.Contains("new HashSet") || classText.Contains("new List") || classText.Contains("new Stack") || classText.Contains("new Queue") || usesHeap
                               || classText.Contains("new()") && (classText.Contains("Dictionary<") || classText.Contains("PriorityQueue<") || classText.Contains("List<"));
        space = usesHash || usesHeap || classCollection && (depth >= 1 || method.Parent is ClassDeclarationSyntax { } c && c.Identifier.Text != "Solution") || allocatesList && depth >= 1 && text.Contains(".Add(")
            ? "O(n) extra: a collection can grow with the input"
            : recursive ? "O(depth) for the call stack" : "O(1) extra (no collection grows with the input)";

        var observations = new List<string>();
        foreach (var (n, conditions, passed) in runs.Where(r => r.N > 0))
        {
            observations.Add($"n = {n}: {conditions} condition check{(conditions == 1 ? "" : "s")} recorded, {(passed ? "passed" : "failed")}.");
        }
        var points = runs.Where(r => r.N > 1).OrderBy(r => r.N).ToList();
        if (points.Count >= 2 && points[^1].N > points[0].N)
        {
            double growth = (double)Math.Max(1, points[^1].Conditions) / Math.Max(1, points[0].Conditions);
            double ratio = (double)points[^1].N / points[0].N;
            observations.Add($"From n = {points[0].N} to n = {points[^1].N} ({ratio:0.#}× larger), checks grew {growth:0.#}×. Small inputs and early returns make this noisy, so treat it as a hint, not proof.");
        }

        var suggestions = new List<string>();
        bool allPassed = runs.Count > 0 && runs.All(r => r.Passed);
        if (runs.Count > 0 && !allPassed)
        {
            suggestions.Add("Get every test passing before thinking about speed. Step through the first failing case and watch the variables.");
        }
        else if (approach.StartsWith("Brute force"))
        {
            suggestions.Add("Your brute force is correct. Understanding it is the right first step.");
            suggestions.Add($"Next challenge: can you avoid the inner loop? Reveal a hint if you're stuck ({hintsAvailable} available).");
        }
        else if (approach == "Hash map lookup" && allPassed)
        {
            suggestions.Add("Hash lookups replace a nested scan here, trading O(n) memory for speed.");
            suggestions.Add("Add a test with duplicate values to make sure the edge cases behave as you expect.");
        }
        else if (allPassed)
        {
            suggestions.Add("All tests pass. Compare the condition-check counts across inputs of different sizes to see how your approach scales.");
        }

        return new(approach, explanation, time, space, confidence, observations, suggestions);
    }

    private static bool IsLoop(SyntaxNode n) => n is ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax;

    /// <summary>A loop whose bound or collection mentions a parameter (e.g. i &lt; nums.Length, foreach x in nums).</summary>
    private static bool IsInputBound(SyntaxNode loop, HashSet<string> parameters)
    {
        SyntaxNode? bound = loop switch
        {
            ForStatementSyntax f => f.Condition,
            WhileStatementSyntax w => w.Condition,
            DoStatementSyntax d => d.Condition,
            ForEachStatementSyntax fe => fe.Expression,
            ForEachVariableStatementSyntax fv => fv.Expression,
            _ => null,
        };
        return bound?.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(id => parameters.Contains(id.Identifier.Text)) == true;
    }
}
