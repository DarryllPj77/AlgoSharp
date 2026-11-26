using AlgoSharp.Playground.Library;

namespace AlgoSharp.Web.Services;

/// <summary>Mutable, form-friendly copy of a <see cref="UserProblem"/> for the preview and edit forms.</summary>
public sealed class ProblemDraft
{
    public sealed class ExampleRow
    {
        public string Input { get; set; } = "";
        public string Output { get; set; } = "";
        public string Explanation { get; set; } = "";
    }

    public sealed class TestRow
    {
        public List<string> Args { get; set; } = [];
        public string Expected { get; set; } = "";
        public bool Verified { get; set; }
    }

    public UserProblem Original { get; private set; } = new();
    public bool Include { get; set; } = true;
    public List<ImportIssue> Issues { get; set; } = [];

    public string Title { get; set; } = "";
    public string Level { get; set; } = "";
    public string Difficulty { get; set; } = "";
    public string Category { get; set; } = "";
    public string Concepts { get; set; } = "";
    public string Collection { get; set; } = "";
    public string Description { get; set; } = "";
    public string InputSpec { get; set; } = "";
    public string OutputSpec { get; set; } = "";
    public string Constraints { get; set; } = "";
    public string Instructions { get; set; } = "";
    public List<ExampleRow> Examples { get; set; } = [];
    public string Signature { get; set; } = "";
    public bool SignatureSuggested { get; set; }
    public string StarterCode { get; set; } = "";
    public List<TestRow> Tests { get; set; } = [];
    public ProblemMode Mode { get; set; }

    public static ProblemDraft From(UserProblem p, IEnumerable<ImportIssue>? issues = null) => new()
    {
        Original = p,
        Issues = [.. issues ?? []],
        Title = p.Title,
        Level = p.Level?.ToString() ?? "",
        Difficulty = p.Difficulty ?? "",
        Category = p.Category ?? "",
        Concepts = string.Join(", ", p.Concepts),
        Collection = p.Collection,
        Description = p.Description,
        InputSpec = p.InputSpec ?? "",
        OutputSpec = p.OutputSpec ?? "",
        Constraints = string.Join("\n", p.Constraints),
        Instructions = p.Instructions ?? "",
        Examples = [.. p.Examples.Select(e => new ExampleRow { Input = e.Input, Output = e.Output, Explanation = e.Explanation ?? "" })],
        Signature = p.Signature ?? "",
        SignatureSuggested = p.SignatureSuggested,
        StarterCode = p.StarterCode ?? "",
        Tests = [.. p.Tests.Select(t => new TestRow { Args = [.. t.Args], Expected = t.Expected, Verified = t.Verified })],
        Mode = p.Mode,
    };

    public UserProblem ToProblem()
    {
        static string? N(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        return Original with
        {
            Title = Title.Trim(),
            Level = int.TryParse(Level, out int l) ? l : null,
            Difficulty = N(Difficulty),
            Category = N(Category),
            Concepts = [.. Concepts.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            Collection = Collection.Trim(),
            Description = Description.Trim(),
            InputSpec = N(InputSpec),
            OutputSpec = N(OutputSpec),
            Constraints = [.. Constraints.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            Instructions = N(Instructions),
            Examples = [.. Examples.Where(e => e.Input.Trim().Length > 0 || e.Output.Trim().Length > 0)
                .Select(e => new UserExample { Input = e.Input.Trim(), Output = e.Output.Trim(), Explanation = N(e.Explanation) })],
            Signature = N(Signature),
            SignatureSuggested = SignatureSuggested && Signature.Trim() == (Original.Signature ?? "").Trim(),
            StarterCode = N(StarterCode),
            Tests = [.. Tests.Select(t => new UserTest { Args = [.. t.Args.Select(a => a.Trim())], Expected = t.Expected.Trim(), Verified = t.Verified })],
            Mode = Mode,
            Revision = Original.Revision + 1,
        };
    }

    /// <summary>Problems that must be fixed before saving.</summary>
    public IEnumerable<string> BlockingErrors()
    {
        if (Title.Trim().Length == 0) yield return "Add a title.";
        if (Description.Trim().Length == 0) yield return "Add a description.";
        if (Level.Trim().Length > 0 && !int.TryParse(Level, out _)) yield return "Level must be a whole number.";
    }
}
