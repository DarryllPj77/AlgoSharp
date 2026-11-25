using AlgoSharp.Core.Engine;

namespace AlgoSharp.Core.Catalog;

public enum Difficulty { Beginner, Intermediate, Advanced }

public sealed record Category(string Key, string Name, string Blurb);

public sealed record Complexity(string Best, string Average, string Worst, string Space, string Note);

public enum ParamKind { Number, Vertex }

public sealed record ParamDef(string Name, string Label, int Default, int Min = -99, int Max = 999, ParamKind Kind = ParamKind.Number);

public sealed record OperationDef(
    string Key,
    string Label,
    string CodeId,
    string Summary,
    Complexity Complexity,
    IReadOnlyList<ParamDef> Params);

public enum InputKind { Numbers, Graph, None }

/// <summary>Constraints for the user-editable data set.</summary>
public sealed record InputSpec(int MinCount, int MaxCount, int MinValue, int MaxValue, int[] Default, bool SortedRequired = false)
{
    public InputKind Kind { get; init; } = InputKind.Numbers;
    public string DefaultText { get; init; } = "";
}

public sealed record AlgorithmDef(
    string Slug,
    string Name,
    Category Category,
    Difficulty Difficulty,
    string Tagline,
    string Description,
    IReadOnlyList<string> KeyIdeas,
    IReadOnlyList<OperationDef> Operations,
    InputSpec Input,
    string Preview,
    Func<int[], IVisualizerSession>? CreateSession,
    int Phase = 1)
{
    public bool Available => CreateSession is not null || CreateFromText is not null;

    /// <summary>For text input (graphs): builds a session or throws <see cref="FormatException"/>.</summary>
    public Func<string, IVisualizerSession>? CreateFromText { get; init; }
}

/// <summary>
/// Owns the live data structure for one visualizer page. Operations run the real C# code and
/// return the recorded steps. For persistent structures (arrays, lists, stacks, queues) the
/// result of one operation is the starting point of the next.
/// </summary>
public interface IVisualizerSession
{
    Step Snapshot(string message);
    int[] Values { get; }
    OperationResult Run(string operation, IReadOnlyDictionary<string, int> args);

    /// <summary>True when operations change the structure (and the next run starts from the result).</summary>
    bool Persistent => true;

    /// <summary>Text form of the input, for text-based data (graphs).</summary>
    string? Text => null;

    /// <summary>Vertex names for vertex parameters.</summary>
    IReadOnlyList<string> Vertices => [];
}

public sealed record OperationResult(IReadOnlyList<Step> Steps, string Summary, bool Failed);
