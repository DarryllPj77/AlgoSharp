namespace AlgoSharp.Core.Engine;

/// <summary>
/// Records steps for linked structures by walking the <em>real</em> nodes on every snapshot.
/// Node identity comes from object references, so pointer changes made by the algorithm are
/// exactly what the UI draws.
/// </summary>
public sealed class ListTracer<TNode> where TNode : class
{
    private readonly Func<TNode?> head;
    private readonly Func<TNode, TNode?> next;
    private readonly Func<TNode, int> value;
    private readonly Dictionary<TNode, int> ids = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, double> lastPos = [];
    private readonly HashSet<TNode> fresh = new(ReferenceEqualityComparer.Instance);
    private int nextId;

    public ListTracer(Func<TNode?> head, Func<TNode, TNode?> next, Func<TNode, int> value)
    {
        this.head = head;
        this.next = next;
        this.value = value;
    }

    public List<Step> Steps { get; private set; } = [];

    public void BeginOperation() => Steps = [];

    /// <summary>Marks a node as newly created for the next recorded step.</summary>
    public void Fresh(TNode node) => fresh.Add(node);

    public ListStepBuilder<TNode> At(string label) => new(this, label);

    public Step Snapshot(string message) => At("").Ptr("head", head()).Say(message, record: false);

    internal int IdOf(TNode node)
    {
        if (!ids.TryGetValue(node, out int id))
        {
            id = nextId++;
            ids[node] = id;
        }
        return id;
    }

    internal Step Emit(ListStepBuilder<TNode> b, bool record)
    {
        var chain = new List<TNode>();
        var seen = new HashSet<TNode>(ReferenceEqualityComparer.Instance);
        for (var n = head(); n is not null && seen.Add(n) && chain.Count < 64; n = next(n)) chain.Add(n);

        var pos = new Dictionary<TNode, double>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < chain.Count; i++) pos[chain[i]] = i;

        // Nodes referenced by a pointer or highlight but no longer reachable from head "float" below the list.
        var floating = new List<TNode>();
        foreach (var n in b.Referenced)
        {
            if (!pos.ContainsKey(n) && !floating.Contains(n, ReferenceEqualityComparer.Instance)) floating.Add(n);
        }

        var nodes = new List<ListNodeView>();
        for (int i = 0; i < chain.Count; i++)
        {
            var n = chain[i];
            int id = IdOf(n);
            lastPos[id] = i;
            nodes.Add(new ListNodeView(id, value(n), i, 0, StateOf(b, n)));
        }
        foreach (var n in floating)
        {
            int id = IdOf(n);
            var target = next(n);
            double p = target is not null && pos.TryGetValue(target, out double tp)
                ? tp - 0.5
                : lastPos.TryGetValue(id, out double lp) ? lp : chain.Count;
            lastPos[id] = p;
            nodes.Add(new ListNodeView(id, value(n), p, 1, StateOf(b, n)));
        }

        var links = new List<ListLinkView>();
        foreach (var n in chain.Concat(floating))
        {
            var t = next(n);
            if (t is null) continue;
            if (!pos.ContainsKey(t) && !floating.Contains(t, ReferenceEqualityComparer.Instance)) continue;
            var state = b.LinkHighlightFor(n) ?? CellState.Default;
            links.Add(new ListLinkView(IdOf(n), IdOf(t), state));
        }

        var pointers = new List<ListPointerView>();
        var named = b.Pointers.Any(p => p.Name == "head")
            ? b.Pointers
            : [("head", head()), .. b.Pointers];
        foreach (var group in named.GroupBy(p => p.Node is null ? -1 : IdOf(p.Node)))
        {
            int k = 0;
            foreach (var p in group) pointers.Add(new ListPointerView(p.Name, group.Key, k++));
        }

        var frame = new ListFrame(nodes, links, pointers, chain.Count);
        var step = new Step(b.Label, b.Message, b.Vars, List: frame, IsError: b.IsError);
        if (record)
        {
            Steps.Add(step);
            fresh.Clear();
        }
        return step;
    }

    private CellState StateOf(ListStepBuilder<TNode> b, TNode n) =>
        b.HighlightFor(n) ?? (fresh.Contains(n) ? CellState.New : CellState.Default);
}

public sealed class ListStepBuilder<TNode> where TNode : class
{
    private readonly ListTracer<TNode> tracer;
    private readonly List<(CellState State, TNode Node)> highlights = [];
    private readonly List<(CellState State, TNode Node)> linkHighlights = [];

    internal ListStepBuilder(ListTracer<TNode> tracer, string label)
    {
        this.tracer = tracer;
        Label = label;
    }

    internal string Label { get; }
    internal string Message { get; private set; } = "";
    internal bool IsError { get; private set; }
    internal List<VarView> Vars { get; } = [];
    internal List<(string Name, TNode? Node)> Pointers { get; } = [];
    internal List<TNode> Referenced { get; } = [];

    public ListStepBuilder<TNode> Hl(CellState state, TNode? node)
    {
        if (node is null) return this;
        highlights.Add((state, node));
        Referenced.Add(node);
        return this;
    }

    /// <summary>Highlights the <c>Next</c> arrow leaving <paramref name="node"/>.</summary>
    public ListStepBuilder<TNode> HlLink(CellState state, TNode? node)
    {
        if (node is not null) linkHighlights.Add((state, node));
        return this;
    }

    public ListStepBuilder<TNode> Ptr(string name, TNode? node)
    {
        Pointers.Add((name, node));
        if (node is not null) Referenced.Add(node);
        return this;
    }

    public ListStepBuilder<TNode> V(string name, object? value)
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

    internal CellState? HighlightFor(TNode n)
    {
        for (int i = highlights.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(highlights[i].Node, n)) return highlights[i].State;
        }
        return null;
    }

    internal CellState? LinkHighlightFor(TNode n)
    {
        for (int i = linkHighlights.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(linkHighlights[i].Node, n)) return linkHighlights[i].State;
        }
        return null;
    }
}
