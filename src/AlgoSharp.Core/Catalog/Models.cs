using AlgoSharp.Core.Engine;

namespace AlgoSharp.Core.Catalog;

public enum Difficulty { Beginner, Intermediate, Advanced }

public sealed record Category(string Key, string Name, string Blurb);

public sealed record Complexity(string Best, string Average, string Worst, string Space, string Note);

public sealed record ParamDef(string Name, string Label, int Default, int Min = -99, int Max = 999);

public sealed record OperationDef(
    string Key,
    string Label,
    string CodeId,
    string Summary,
    Complexity Complexity,
    IReadOnlyList<ParamDef> Params);

/// <summary>Constraints for the user-editable data set.</summary>
public sealed record InputSpec(int MinCount, int MaxCount, int MinValue, int MaxValue, int[] Default, bool SortedRequired = false);

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
    public bool Available => CreateSession is not null;
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
}

public sealed record OperationResult(IReadOnlyList<Step> Steps, string Summary, bool Failed);
