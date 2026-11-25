namespace AlgoSharp.Core.Engine;

/// <summary>Layout helpers that turn real tree objects into positioned scene nodes.</summary>
public static class TreeLayout
{
    private const double Width = 100, Margin = 4, MaxSpacing = 11, Top = 8, LevelGap = 13;

    /// <summary>Binary tree: x from in-order rank (so keys read left to right), y from depth.</summary>
    public static void Binary<TNode>(SceneBuilder sb, TNode? root, Func<TNode, TNode?> left, Func<TNode, TNode?> right, Func<TNode, string> label)
        where TNode : class
    {
        var order = new List<(TNode Node, int Depth)>();
        void Walk(TNode? n, int depth)
        {
            if (n is null || order.Count > 63) return;
            Walk(left(n), depth + 1);
            order.Add((n, depth));
            Walk(right(n), depth + 1);
        }
        Walk(root, 0);

        double spacing = order.Count == 0 ? 0 : Math.Min(MaxSpacing, (Width - 2 * Margin) / order.Count);
        double start = (Width - spacing * order.Count) / 2;
        int maxDepth = 0;
        for (int i = 0; i < order.Count; i++)
        {
            var (n, d) = order[i];
            maxDepth = Math.Max(maxDepth, d);
            sb.Node(n, label(n), start + (i + 0.5) * spacing, Top + d * LevelGap);
            if (left(n) is { } l) sb.Edge(l, n, l);
            if (right(n) is { } r) sb.Edge(r, n, r);
        }
        sb.Height = Math.Max(30, Top + maxDepth * LevelGap + 12);
    }

    /// <summary>
    /// Tidy n-ary (forest) layout: leaves get consecutive slots, parents sit centred over
    /// their children. <paramref name="edgesUp"/> draws child → parent arrows (union–find).
    /// </summary>
    public static void Nary<TNode>(
        SceneBuilder sb, IEnumerable<TNode> roots, Func<TNode, IEnumerable<TNode>> children,
        Func<TNode, string> label, Func<TNode, string?>? badge = null,
        NodeShape shape = NodeShape.Circle, bool directed = false, bool edgesUp = false, double levelGap = 12)
        where TNode : notnull
    {
        int leaves = 0;
        var placed = new List<(TNode Node, TNode? Parent, bool HasParent, double Slot, int Depth)>();
        double Place(TNode n, TNode? parent, bool hasParent, int depth)
        {
            var kids = children(n).ToList();
            double slot;
            if (kids.Count == 0 || depth > 12) slot = leaves++;
            else
            {
                var xs = kids.Select(k => Place(k, n, true, depth + 1)).ToList();
                slot = (xs[0] + xs[^1]) / 2;
            }
            placed.Add((n, parent, hasParent, slot, depth));
            return slot;
        }
        foreach (var r in roots) Place(r, default, false, 0);

        double spacing = leaves == 0 ? 0 : Math.Min(MaxSpacing + 2, (Width - 2 * Margin) / leaves);
        double start = (Width - spacing * leaves) / 2;
        int maxDepth = 0;
        foreach (var (n, parent, hasParent, slot, depth) in placed)
        {
            maxDepth = Math.Max(maxDepth, depth);
            sb.Node(n, label(n), start + (slot + 0.5) * spacing, Top - 1 + depth * levelGap, shape, badge?.Invoke(n));
            if (hasParent && parent is not null)
            {
                if (edgesUp) sb.Edge(n, n, parent, directed: directed);
                else sb.Edge(n, parent, n, directed: directed);
            }
        }
        sb.Height = Math.Max(28, Top - 1 + maxDepth * levelGap + 13);
    }

    /// <summary>Complete binary tree stored in an array (heaps): node i has children 2i+1 and 2i+2.</summary>
    public static GraphFrame HeapTree(ArrayFrame array)
    {
        var cells = array.Cells.Where(c => c.Row == 0).ToList();
        var nodes = cells.Select(c =>
        {
            var (x, y) = HeapPosition(c.Pos);
            return new GNode("c" + c.Id, c.Value.ToString(), x, y, NodeShape.Circle, c.State, $"[{c.Pos}]");
        }).ToList();
        var byPos = cells.Where(c => c.State != CellState.Removed).GroupBy(c => c.Pos).ToDictionary(g => g.Key, g => g.First());
        var edges = new List<GEdge>();
        foreach (var (p, child) in byPos)
        {
            if (p == 0 || !byPos.TryGetValue((p - 1) / 2, out var parent)) continue;
            bool hot = child.State is CellState.Swap or CellState.Compare && parent.State is CellState.Swap or CellState.Compare;
            edges.Add(new GEdge("he" + p, "c" + parent.Id, "c" + child.Id, null, hot ? CellState.Active : CellState.Default, false));
        }
        var pointers = array.Pointers
            .Where(p => p.Row == 0 && byPos.ContainsKey(p.Pos))
            .Select(p => new GPointer(p.Name, "c" + byPos[p.Pos].Id, p.Stack))
            .ToList();
        int maxPos = cells.Count == 0 ? 0 : cells.Max(c => c.Pos);
        int depth = (int)Math.Floor(Math.Log2(maxPos + 1));
        return new GraphFrame(Math.Max(30, Top + Math.Max(depth, 1) * LevelGap + 12), nodes, edges, pointers);
    }

    public static (double X, double Y) HeapPosition(int index)
    {
        int level = (int)Math.Floor(Math.Log2(index + 1));
        int first = (1 << level) - 1;
        int slots = 1 << level;
        double x = Margin + (index - first + 0.5) * (Width - 2 * Margin) / slots;
        return (x, Top + level * LevelGap);
    }
}

/// <summary>
/// Records a recursion tree: every call becomes a node under its caller. The algorithm
/// calls <see cref="Enter"/> on entry and <see cref="Return"/> before returning.
/// </summary>
public sealed class CallTreeTracer
{
    private sealed class Call
    {
        public required string Label;
        public Call? Parent;
        public readonly List<Call> Children = [];
        public string? Result;
        public bool Memo;
    }

    private readonly List<Call> roots = [];
    private readonly Stack<Call> stack = new();
    private readonly GraphTracer graph;
    private Call? justReturned;

    public CallTreeTracer()
    {
        graph = new GraphTracer(Layout);
    }

    public List<Step> Steps => graph.Steps;
    public int Depth => stack.Count;
    public int Calls { get; private set; }

    public void Enter(string label)
    {
        var call = new Call { Label = label, Parent = stack.Count > 0 ? stack.Peek() : null };
        if (call.Parent is null) roots.Add(call); else call.Parent.Children.Add(call);
        stack.Push(call);
        justReturned = null;
        Calls++;
        Restyle();
    }

    public void Return(object? value, bool memo = false)
    {
        var call = stack.Pop();
        justReturned = call;
        call.Result = ArrayStepBuilder.Format(value);
        call.Memo = memo;
        graph.Badge(call, memo ? $"= {call.Result} (memo)" : $"= {call.Result}");
        Restyle();
        if (stack.Count > 0) graph.Mark(memo ? CellState.Found : CellState.Sorted, call);
    }

    public GraphStepBuilder At(string label)
    {
        var b = graph.At(label);
        if (justReturned is not null) b.Hl(CellState.Key, justReturned);
        else if (stack.Count > 0) b.Hl(CellState.Key, stack.Peek());
        justReturned = null;
        return b;
    }

    public Step Snapshot(string message) => graph.Snapshot(message);

    /// <summary>Calls on the stack are "active"; finished calls keep their result.</summary>
    private void Restyle()
    {
        foreach (var c in stack) graph.Mark(CellState.Active, c);
        foreach (var r in roots) Visit(r);
        void Visit(Call c)
        {
            if (c.Result is not null) graph.Mark(c.Memo ? CellState.Found : CellState.Sorted, c);
            foreach (var ch in c.Children) Visit(ch);
        }
    }

    private void Layout(SceneBuilder sb) =>
        TreeLayout.Nary(sb, roots, c => c.Children, c => c.Label, shape: NodeShape.Call, directed: true);
}
