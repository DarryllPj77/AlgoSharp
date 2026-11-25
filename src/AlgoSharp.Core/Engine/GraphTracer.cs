namespace AlgoSharp.Core.Engine;

/// <summary>
/// Collects the nodes and edges of the <em>real</em> data structure for one snapshot.
/// Keys identify nodes/edges across snapshots: object references for node classes,
/// values for ints and edge tuples. Stable keys let the UI animate movement.
/// </summary>
public sealed class SceneBuilder
{
    internal readonly List<(object Key, string Label, double X, double Y, NodeShape Shape, string? Badge)> Nodes = [];
    internal readonly List<(object Key, object From, object To, string? Label, bool Directed)> Edges = [];

    public double Height { get; set; } = 50;

    public void Node(object key, string label, double x, double y, NodeShape shape = NodeShape.Circle, string? badge = null) =>
        Nodes.Add((key, label, x, y, shape, badge));

    public void Edge(object key, object from, object to, string? label = null, bool directed = false) =>
        Edges.Add((key, from, to, label, directed));
}

/// <summary>Undirected edge identity, normalised so (a, b) and (b, a) are the same edge.</summary>
public readonly record struct EdgeKey(int A, int B)
{
    public static EdgeKey Of(int u, int v) => u < v ? new(u, v) : new(v, u);
}

/// <summary>Records steps for anything drawn as positioned nodes and edges.</summary>
public sealed class GraphTracer
{
    private readonly Action<SceneBuilder> scene;
    private readonly Dictionary<object, string> nodeIds = [];
    private readonly Dictionary<object, string> edgeIds = [];
    private readonly Dictionary<object, CellState> nodeMarks = [];
    private readonly Dictionary<object, CellState> edgeMarks = [];
    private readonly Dictionary<object, string> badges = [];
    private readonly Dictionary<object, string> edgeLabels = [];
    private readonly HashSet<object> revealed = [];
    private int next;

    /// <summary>When true, only nodes passed to <see cref="Reveal"/> are drawn (e.g. while a tree is being built).</summary>
    public bool HideUnrevealed { get; set; }

    public void Reveal(object node) => revealed.Add(node);

    public void RevealAll()
    {
        HideUnrevealed = false;
        revealed.Clear();
    }

    /// <summary>Overrides an edge's label (e.g. "flow/capacity").</summary>
    public void EdgeLabel(object edge, string? text)
    {
        if (text is null) edgeLabels.Remove(edge); else edgeLabels[edge] = text;
    }

    public GraphTracer(Action<SceneBuilder> scene) => this.scene = scene;

    public List<Step> Steps { get; private set; } = [];

    public void BeginOperation()
    {
        Steps = [];
        ClearMarks();
    }

    public void Mark(CellState state, object? node)
    {
        if (node is not null) nodeMarks[node] = state;
    }

    public void MarkEdge(CellState state, object? edge)
    {
        if (edge is not null) edgeMarks[edge] = state;
    }

    public void Unmark(object? node)
    {
        if (node is not null) nodeMarks.Remove(node);
    }

    public void Badge(object node, string? text)
    {
        if (text is null) badges.Remove(node); else badges[node] = text;
    }

    public void BadgeEach(int count, Func<int, string?> text)
    {
        for (int i = 0; i < count; i++) Badge(i, text(i));
    }

    /// <summary>Applies a tracer action to many items (keeps instrumentation on one hidden line).</summary>
    public void ForEach<TItem>(IEnumerable<TItem> items, Action<TItem> action)
    {
        foreach (var item in items.ToList()) action(item);
    }

    public void ClearMarks()
    {
        nodeMarks.Clear();
        edgeMarks.Clear();
        badges.Clear();
        edgeLabels.Clear();
    }

    public GraphStepBuilder At(string label) => new(this, label);

    public Step Snapshot(string message) => At("").Say(message, record: false);

    internal string NodeId(object key)
    {
        if (!nodeIds.TryGetValue(key, out var id)) nodeIds[key] = id = "n" + next++;
        return id;
    }

    private string EdgeId(object key)
    {
        if (!edgeIds.TryGetValue(key, out var id)) edgeIds[key] = id = "e" + next++;
        return id;
    }

    internal Step Emit(GraphStepBuilder b, bool record)
    {
        var sb = new SceneBuilder();
        scene(sb);

        var visible = HideUnrevealed ? sb.Nodes.Where(n => revealed.Contains(n.Key)).ToList() : sb.Nodes;
        var nodes = visible.Select(n => new GNode(
            NodeId(n.Key), n.Label, n.X, n.Y, n.Shape,
            b.NodeHighlight(n.Key) ?? (nodeMarks.TryGetValue(n.Key, out var m) ? m : CellState.Default),
            badges.GetValueOrDefault(n.Key) ?? n.Badge)).ToList();

        var present = visible.Select(n => n.Key).ToHashSet();
        var edges = sb.Edges
            .Where(e => present.Contains(e.From) && present.Contains(e.To))
            .Select(e => new GEdge(
                EdgeId(e.Key), NodeId(e.From), NodeId(e.To), edgeLabels.GetValueOrDefault(e.Key) ?? e.Label,
                b.EdgeHighlight(e.Key) ?? (edgeMarks.TryGetValue(e.Key, out var m) ? m : CellState.Default),
                e.Directed)).ToList();

        var pointers = new List<GPointer>();
        foreach (var group in b.Pointers.Where(p => p.Node is not null && present.Contains(p.Node)).GroupBy(p => p.Node!))
        {
            int k = 0;
            foreach (var p in group) pointers.Add(new GPointer(p.Name, NodeId(group.Key), k++));
        }

        var step = new Step(b.Label, b.Message, b.Vars, IsError: b.IsError, Graph: new GraphFrame(sb.Height, nodes, edges, pointers));
        if (record) Steps.Add(step);
        return step;
    }
}

public sealed class GraphStepBuilder
{
    private readonly GraphTracer tracer;
    private readonly List<(CellState State, object Key)> nodes = [];
    private readonly List<(CellState State, object Key)> edges = [];

    internal GraphStepBuilder(GraphTracer tracer, string label)
    {
        this.tracer = tracer;
        Label = label;
    }

    internal string Label { get; }
    internal string Message { get; private set; } = "";
    internal bool IsError { get; private set; }
    internal List<VarView> Vars { get; } = [];
    internal List<(string Name, object? Node)> Pointers { get; } = [];

    public GraphStepBuilder Hl(CellState state, object? node)
    {
        if (node is not null) nodes.Add((state, node));
        return this;
    }

    public GraphStepBuilder HlEdge(CellState state, object? edge)
    {
        if (edge is not null) edges.Add((state, edge));
        return this;
    }

    public GraphStepBuilder HlEdges(CellState state, IEnumerable<object> edges)
    {
        foreach (var e in edges) HlEdge(state, e);
        return this;
    }

    public GraphStepBuilder Ptr(string name, object? node)
    {
        Pointers.Add((name, node));
        return this;
    }

    public GraphStepBuilder V(string name, object? value)
    {
        Vars.Add(new VarView(name, ArrayStepBuilder.Format(value)));
        return this;
    }

    public Step Say(string message, bool record = true)
    {
        Message = message;
        return tracer.Emit(this, record);
    }

    public Step Error(string message)
    {
        IsError = true;
        return Say(message);
    }

    internal CellState? NodeHighlight(object key)
    {
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            if (Equals(nodes[i].Key, key)) return nodes[i].State;
        }
        return null;
    }

    internal CellState? EdgeHighlight(object key)
    {
        for (int i = edges.Count - 1; i >= 0; i--)
        {
            if (Equals(edges[i].Key, key)) return edges[i].State;
        }
        return null;
    }
}
