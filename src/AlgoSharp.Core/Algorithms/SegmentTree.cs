using AlgoSharp.Core.Engine;
using static AlgoSharp.Core.Engine.CellState;

namespace AlgoSharp.Core.Algorithms;

/// <summary>
/// Sum segment tree stored in an array: node i covers a range, its children 2i and 2i+1
/// cover the two halves. Range queries and point updates are O(log n).
/// </summary>
public sealed class SegmentTree
{
    private readonly int[] data;
    private readonly int[] tree;
    private readonly int n;

    public SegmentTree(int[] values)
    {
        data = (int[])values.Clone();
        n = data.Length;
        tree = new int[4 * Math.Max(1, n)];
        T = new GraphTracer(Scene);
        Build();
        T.RevealAll();
        T.BeginOperation();
    }

    public GraphTracer T { get; }
    public int Length => n;
    public int[] Values => (int[])data.Clone();
    public int Root => tree[1];

    private void Scene(SceneBuilder sb)
    {
        int maxDepth = 0;
        void Add(int node, int lo, int hi, int depth, int parent)
        {
            maxDepth = Math.Max(maxDepth, depth);
            double x = 4 + ((lo + hi) / 2.0 + 0.5) * 92.0 / n;
            sb.Node(node, tree[node].ToString(), x, 7 + depth * 12, badge: lo == hi ? $"[{lo}]" : $"[{lo}..{hi}]");
            if (parent > 0) sb.Edge(node, parent, node);
            if (lo == hi) return;
            int mid = (lo + hi) / 2;
            Add(2 * node, lo, mid, depth + 1, node);
            Add(2 * node + 1, mid + 1, hi, depth + 1, node);
        }
        if (n > 0) Add(1, 0, n - 1, 0, 0);
        sb.Height = 7 + maxDepth * 12 + 13;
    }

    // <code id="seg-build">
    public void Build()
    {
        T.HideUnrevealed = true;
        Build(1, 0, n - 1); // #start
        T.At("start").Hl(Found, 1).V("tree[1]", tree[1]).Say($"Built! The root holds the sum of everything: {tree[1]}. Building visits each of the ~2n nodes once: O(n).");
    }

    private void Build(int node, int lo, int hi)
    {
        T.Reveal(node);
        if (lo == hi) // #leaf
        {
            tree[node] = data[lo]; // #leafSet
            T.At("leafSet").Hl(New, node).Ptr("node", node).V("node", node).V("lo", lo).V("data[lo]", data[lo]).Say($"Leaf for index {lo}: copy data[{lo}] = {data[lo]}.");
            return; // #leafRet
        }
        int mid = (lo + hi) / 2; // #mid
        T.At("mid").Hl(Active, node).Ptr("node", node).V("node", node).V("lo", lo).V("hi", hi).V("mid", mid).Say($"Node {node} covers [{lo}..{hi}]. Split at mid = {mid}: build both halves first.");
        Build(2 * node, lo, mid); // #left
        Build(2 * node + 1, mid + 1, hi); // #right
        tree[node] = tree[2 * node] + tree[2 * node + 1]; // #sum
        T.At("sum").Hl(New, node).Hl(Compare, 2 * node).Hl(Compare, 2 * node + 1).Ptr("node", node).V("node", node).V("left", tree[2 * node]).V("right", tree[2 * node + 1]).V("tree[node]", tree[node]).Say($"[{lo}..{hi}] = {tree[2 * node]} + {tree[2 * node + 1]} = {tree[node]}.");
    }
    // </code>

    // <code id="seg-query">
    public int Query(int l, int r)
    {
        return Query(1, 0, n - 1, l, r); // #start
    }

    private int Query(int node, int lo, int hi, int l, int r)
    {
        if (r < lo || hi < l) // #none
        {
            T.Mark(Dim, node);
            T.At("retNone").Hl(Dim, node).Ptr("node", node).V("node range", $"[{lo}..{hi}]").V("query", $"[{l}..{r}]").Say($"[{lo}..{hi}] doesn't overlap [{l}..{r}]: contributes 0.");
            return 0; // #retNone
        }
        if (l <= lo && hi <= r) // #full
        {
            T.Mark(Found, node);
            T.At("retFull").Hl(Found, node).Ptr("node", node).V("node range", $"[{lo}..{hi}]").V("query", $"[{l}..{r}]").V("tree[node]", tree[node]).Say($"[{lo}..{hi}] lies fully inside [{l}..{r}]: use its stored sum {tree[node]} without going deeper.");
            return tree[node]; // #retFull
        }
        T.Mark(Active, node);
        T.At("mid").Hl(Key, node).Ptr("node", node).V("node range", $"[{lo}..{hi}]").V("query", $"[{l}..{r}]").Say($"[{lo}..{hi}] partially overlaps [{l}..{r}]: ask both children.");
        int mid = (lo + hi) / 2; // #mid
        int left = Query(2 * node, lo, mid, l, r); // #left
        int right = Query(2 * node + 1, mid + 1, hi, l, r); // #right
        T.At("ret").Hl(Key, node).Ptr("node", node).V("left", left).V("right", right).V("sum", left + right).Say($"Combine at [{lo}..{hi}]: {left} + {right} = {left + right}.");
        return left + right; // #ret
    }
    // </code>

    // <code id="seg-update">
    public void Update(int index, int value)
    {
        Update(1, 0, n - 1, index, value); // #start
    }

    private void Update(int node, int lo, int hi, int index, int value)
    {
        if (lo == hi) // #leaf
        {
            data[index] = value; // #setData
            tree[node] = value; // #setLeaf
            T.Mark(Active, node);
            T.At("setLeaf").Hl(New, node).Ptr("node", node).V("index", index).V("value", value).Say($"Reached the leaf for index {index}: set it to {value}.");
            return; // #ret
        }
        int mid = (lo + hi) / 2; // #mid
        T.Mark(Active, node);
        T.At("which").Hl(Key, node).Ptr("node", node).V("node range", $"[{lo}..{hi}]").V("mid", mid).V("index", index).Say(index <= mid ? $"Index {index} ≤ mid {mid}: it lives in the left half." : $"Index {index} > mid {mid}: it lives in the right half.");
        if (index <= mid) // #which
        {
            Update(2 * node, lo, mid, index, value); // #left
        }
        else
        {
            Update(2 * node + 1, mid + 1, hi, index, value); // #right
        }
        tree[node] = tree[2 * node] + tree[2 * node + 1]; // #sum
        T.At("sum").Hl(New, node).Ptr("node", node).V("left", tree[2 * node]).V("right", tree[2 * node + 1]).V("tree[node]", tree[node]).Say($"Recompute [{lo}..{hi}] = {tree[2 * node]} + {tree[2 * node + 1]} = {tree[node]}. Only one root-to-leaf path changes: O(log n).");
    }
    // </code>
}
