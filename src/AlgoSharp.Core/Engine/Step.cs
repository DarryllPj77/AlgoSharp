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

public sealed record CellView(int Id, int Value, int Pos, int Row, CellState State)
{
    /// <summary>Text to display instead of <see cref="Value"/> (strings, chars, traced values).</summary>
    public string? Label { get; init; }
}

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

/// <summary>Node shapes understood by the graph renderer.</summary>
public enum NodeShape { Circle, Slot, Call }

/// <summary>A positioned node. Coordinates are logical units on a 100-wide canvas.</summary>
public sealed record GNode(string Id, string Label, double X, double Y, NodeShape Shape, CellState State, string? Badge);

public sealed record GEdge(string Id, string From, string To, string? Label, CellState State, bool Directed);

public sealed record GPointer(string Name, string NodeId, int Stack);

/// <summary>Trees, graphs, hash tables and call trees all render as positioned nodes plus edges.</summary>
public sealed record GraphFrame(
    double Height,
    IReadOnlyList<GNode> Nodes,
    IReadOnlyList<GEdge> Edges,
    IReadOnlyList<GPointer> Pointers);

public sealed record TableCell(string Value, CellState State);

public sealed record MapEntryView(string Key, string Value, CellState State);

/// <summary>A live view of a real Dictionary, plus the key being looked up (if any).</summary>
public sealed record MapFrame(string Title, IReadOnlyList<MapEntryView> Entries, string? ProbeKey, bool? ProbeHit);

/// <summary>A short highlighted expression shown over the visualization, e.g. "2 + 7 = 9".</summary>
public sealed record Callout(string Text, CellState Tone);

/// <summary>A dynamic-programming table: headers plus a grid of cells.</summary>
public sealed record TableFrame(
    IReadOnlyList<string> RowHeaders,
    IReadOnlyList<string> ColHeaders,
    IReadOnlyList<IReadOnlyList<TableCell>> Rows,
    string Corner);

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
    bool IsError = false,
    GraphFrame? Graph = null,
    TableFrame? Table = null)
{
    /// <summary>Live operation counters (comparisons, lookups, ...) at this step.</summary>
    public IReadOnlyList<VarView> Counters { get; init; } = [];
    public MapFrame? Map { get; init; }
    public Callout? Callout { get; init; }
}
