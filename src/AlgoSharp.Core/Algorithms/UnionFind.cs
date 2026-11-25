using AlgoSharp.Core.Engine;
using static AlgoSharp.Core.Engine.CellState;

namespace AlgoSharp.Core.Algorithms;

/// <summary>Disjoint-set forest with union by rank and path compression.</summary>
public sealed class UnionFind
{
    private readonly int[] parent;
    private readonly int[] rank;

    public UnionFind(int size)
    {
        parent = new int[size];
        rank = new int[size];
        for (int i = 0; i < size; i++) parent[i] = i;
        T = new GraphTracer(Scene);
    }

    public GraphTracer T { get; }
    public int Size => parent.Length;
    public int[] Parents => (int[])parent.Clone();
    public int Sets => Enumerable.Range(0, parent.Length).Count(i => parent[i] == i);

    /// <summary>Each set is drawn as a tree with arrows from child to parent.</summary>
    private void Scene(SceneBuilder sb)
    {
        var roots = Enumerable.Range(0, parent.Length).Where(i => parent[i] == i).ToList();
        TreeLayout.Nary(sb, roots,
            x => Enumerable.Range(0, parent.Length).Where(c => c != x && parent[c] == x),
            x => x.ToString(),
            x => parent[x] == x && rank[x] > 0 ? $"rank {rank[x]}" : null,
            directed: true, edgesUp: true, levelGap: 13);
    }

    // <code id="union-find">
    public int Find(int x)
    {
        T.At("check").Hl(Compare, x).Ptr("x", x).V("x", x).V("parent[x]", parent[x]).Say(parent[x] == x ? $"parent[{x}] = {x}: {x} is a root, the representative of its set." : $"parent[{x}] = {parent[x]}: {x} is not a root, keep climbing.");
        if (parent[x] != x) // #check
        {
            parent[x] = Find(parent[x]); // #compress
            T.At("compress").Hl(New, x).Ptr("x", x).V("x", x).V("parent[x]", parent[x]).Say($"Path compression: point {x} straight at the root {parent[x]}, so the next Find is faster.");
        }
        return parent[x]; // #ret
    }

    public bool Union(int a, int b)
    {
        int ra = Find(a); // #findA
        int rb = Find(b); // #findB
        T.At("findB").Hl(Key, ra).Hl(Key, rb).Ptr("ra", ra).Ptr("rb", rb).V("a", a).V("b", b).V("ra", ra).V("rb", rb).Say(ra == rb ? $"{a} and {b} have the same root {ra}." : $"Roots: {a} → {ra}, {b} → {rb}.");
        if (ra == rb) // #same
        {
            T.At("retSame").Hl(Found, ra).V("ra", ra).Say("They are already in the same set: nothing to do.");
            return false; // #retSame
        }
        if (rank[ra] < rank[rb]) (ra, rb) = (rb, ra); // #swap
        parent[rb] = ra; // #link
        T.At("link").Hl(Key, ra).Hl(New, rb).HlEdge(Active, rb).Ptr("ra", ra).Ptr("rb", rb).V("ra", ra).V("rb", rb).V("rank[ra]", rank[ra]).V("rank[rb]", rank[rb]).Say($"Union by rank: hang the shorter tree (root {rb}) under the taller one (root {ra}). This keeps trees shallow.");
        if (rank[ra] == rank[rb]) rank[ra]++; // #rank
        T.At("rank").Hl(Key, ra).V("rank[ra]", rank[ra]).Say($"rank[{ra}] is now {rank[ra]}. A rank only grows when two trees of equal rank merge, so trees stay O(log n) tall.");
        return true; // #ret
    }

    public bool Connected(int a, int b)
    {
        bool same = Find(a) == Find(b); // #connected
        T.At("connected").Hl(same ? Found : Compare, a).Hl(same ? Found : Compare, b).V("a", a).V("b", b).V("same root", same).Say(same ? $"{a} and {b} share a root: they are connected." : $"Different roots: {a} and {b} are in different sets.");
        return same; // #retConnected
    }
    // </code>
}
