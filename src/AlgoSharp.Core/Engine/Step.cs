namespace AlgoSharp.Core.Engine;

/// <summary>Visual role of an element in a single step.</summary>
public enum CellState
{
    Default,
    Compare,
    Swap,
    Sorted,
    Pivot,
    Key,
    Min,
    Found,
    New,
    Removed,
    Active,
    Visited,
    Dim,
    Error,
}

public enum ArrayLayout
{
    /// <summary>Vertical bars whose height encodes the value (sorting).</summary>
    Bars,
    /// <summary>Horizontal row of boxes with slot indices (arrays, queues, searching).</summary>
    Cells,
    /// <summary>Vertical column of boxes growing upwards (stacks).</summary>
    Stack,
}

public sealed record CellView(int Id, int Value, int Pos, int Row, CellState State);

public sealed record PointerView(string Name, int Pos, int Row, int Stack);

public sealed record ArrayFrame(
    ArrayLayout Layout,
    int Capacity,
    IReadOnlyList<CellView> Cells,
    IReadOnlyList<PointerView> Pointers,
    int RangeLo,
    int RangeHi,
    bool HasAux,
    int MaxValue);

public sealed record ListNodeView(int Id, int Value, double Pos, int Row, CellState State);

public sealed record ListLinkView(int FromId, int ToId, CellState State);

public sealed record ListPointerView(string Name, int NodeId, int Stack);

public sealed record ListFrame(
    IReadOnlyList<ListNodeView> Nodes,
    IReadOnlyList<ListLinkView> Links,
    IReadOnlyList<ListPointerView> Pointers,
    int Length);

public sealed record VarView(string Name, string Value);

/// <summary>
/// One deterministic snapshot of an algorithm's execution. Steps are complete snapshots,
/// so the UI can jump to any index (forwards or backwards) without replaying.
/// </summary>
public sealed record Step(
    string Label,
    string Message,
    IReadOnlyList<VarView> Vars,
    ArrayFrame? Array = null,
    ListFrame? List = null,
    bool IsError = false);
