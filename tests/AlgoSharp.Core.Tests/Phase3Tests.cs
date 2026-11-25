using AlgoSharp.Core.Algorithms;
using AlgoSharp.Core.Catalog;
using AlgoSharp.Core.Engine;

namespace AlgoSharp.Core.Tests;

public class Phase3StructureTests
{
    [Fact]
    public void Segment_tree_matches_brute_force()
    {
        var rng = new Random(17);
        for (int trial = 0; trial < 20; trial++)
        {
            int[] data = Enumerable.Range(0, rng.Next(1, 9)).Select(_ => rng.Next(-20, 50)).ToArray();
            var tree = new SegmentTree(data);
            for (int k = 0; k < 40; k++)
            {
                tree.T.BeginOperation();
                if (rng.Next(2) == 0)
                {
                    int i = rng.Next(data.Length), v = rng.Next(-20, 50);
                    tree.Update(i, v);
                    data[i] = v;
                }
                int l = rng.Next(data.Length), r = rng.Next(l, data.Length);
                Assert.Equal(data[l..(r + 1)].Sum(), tree.Query(l, r));
                Assert.Equal(data.Sum(), tree.Root);
            }
        }
        var frame = new SegmentTree([5, 8, 6, 3]).T.Snapshot("").Graph!;
        Assert.Equal(7, frame.Nodes.Count);
        Assert.Equal(6, frame.Edges.Count);
    }

    [Fact]
    public void Segment_tree_build_reveals_nodes_progressively()
    {
        var session = new SegmentTreeSession([1, 2, 3, 4]);
        var steps = session.Run("build", new Dictionary<string, int>()).Steps;
        Assert.True(steps[0].Graph!.Nodes.Count < 7);
        Assert.Equal(7, steps[^1].Graph!.Nodes.Count);
        Assert.Equal("10", steps[^1].Graph!.Nodes.Single(n => n.Badge == "[0..3]").Label);
    }

    [Fact]
    public void Union_find_matches_naive_components()
    {
        var rng = new Random(2);
        var uf = new UnionFind(10);
        var label = Enumerable.Range(0, 10).ToArray();
        for (int i = 0; i < 200; i++)
        {
            uf.T.BeginOperation();
            int a = rng.Next(10), b = rng.Next(10);
            bool expected = label[a] != label[b];
            Assert.Equal(expected, uf.Union(a, b));
            int from = label[b], to = label[a];
            for (int k = 0; k < 10; k++) if (label[k] == from) label[k] = to;
            int x = rng.Next(10), y = rng.Next(10);
            Assert.Equal(label[x] == label[y], uf.Connected(x, y));
        }
        var frame = uf.T.Snapshot("").Graph!;
        Assert.Equal(10 - uf.Sets, frame.Edges.Count);
        var parents = uf.Parents;
        Assert.All(Enumerable.Range(0, 10), i => Assert.Equal(uf.Find(parents[i]), uf.Find(i)));
    }

    [Fact]
    public void Trie_matches_hash_set()
    {
        var pool = new[] { "car", "cart", "care", "cat", "do", "dog", "dot", "a", "an", "ant" };
        var rng = new Random(8);
        var trie = new Trie([]);
        var set = new HashSet<string>();
        for (int i = 0; i < 60; i++)
        {
            trie.T.BeginOperation();
            string w = pool[rng.Next(pool.Length)];
            if (rng.Next(2) == 0) { trie.Insert(w); set.Add(w); }
            Assert.Equal(set.Contains(w), trie.Contains(w));
            string prefix = w[..rng.Next(1, w.Length + 1)];
            Assert.Equal(set.Any(s => s.StartsWith(prefix, StringComparison.Ordinal)), trie.StartsWith(prefix));
        }
        Assert.Equal(set.Order(StringComparer.Ordinal), trie.Words());
        Assert.Throws<FormatException>(() => TrieSession.Parse("hello1"));
    }
}

public class Phase3AlgorithmTests
{
    [Theory]
    [InlineData("ABCBDAB", "BDCABA", 4)]
    [InlineData("AAAA", "AA", 2)]
    [InlineData("ABC", "XYZ", 0)]
    public void Lcs_length_and_subsequence(string a, string b, int length)
    {
        string lcs = DynamicProgramming.Lcs(a, b, DynamicProgramming.LcsTable(a, b));
        Assert.Equal(length, lcs.Length);
        static bool IsSubsequence(string s, string of)
        {
            int i = 0;
            foreach (char c in of) if (i < s.Length && s[i] == c) i++;
            return i == s.Length;
        }
        Assert.True(IsSubsequence(lcs, a));
        Assert.True(IsSubsequence(lcs, b));
    }

    [Theory]
    [InlineData(new[] { 1, 3, 4 }, 6, 2)]
    [InlineData(new[] { 2 }, 3, -1)]
    [InlineData(new[] { 1, 5, 10 }, 18, 5)]
    public void Coin_change(int[] coins, int amount, int expected)
    {
        Assert.Equal(expected, DynamicProgramming.MinCoins(coins, amount, DynamicProgramming.CoinTable(amount)));
    }

    [Fact]
    public void Fib_table_and_steps()
    {
        var t = DynamicProgramming.FibTable(10);
        Assert.Equal(55, DynamicProgramming.Fib(10, t));
        Assert.Equal("55", t.Steps[^1].Table!.Rows[0][10].Value);
        Assert.Equal("", DynamicProgramming.FibTable(3).Snapshot("").Table!.Rows[0][2].Value);
    }

    [Fact]
    public void Max_flow_matches_reference_ford_fulkerson()
    {
        var rng = new Random(31);
        for (int trial = 0; trial < 40; trial++)
        {
            var g = trial == 0 ? Graph.Parse(Graph.FlowText, directed: true) : Graph.Random(rng, directed: true);
            int sink = g.VertexCount - 1 >= 6 ? 6 : g.VertexCount - 1;
            int flow = GraphAlgorithms.MaxFlow(g, 0, sink, new GraphTracer(g.Scene));
            Assert.Equal(ReferenceMaxFlow(g, 0, sink), flow);
        }
    }

    private static int ReferenceMaxFlow(Graph g, int s, int t)
    {
        int n = g.VertexCount;
        var res = new int[n, n];
        foreach (var (u, v, w) in g.Edges) res[u, v] += w;
        int total = 0;
        while (true)
        {
            var seen = new bool[n];
            int pushed = Dfs(s, int.MaxValue);
            if (pushed == 0) return total;
            total += pushed;

            int Dfs(int u, int f)
            {
                if (u == t) return f;
                seen[u] = true;
                for (int v = 0; v < n; v++)
                {
                    if (seen[v] || res[u, v] <= 0) continue;
                    int d = Dfs(v, Math.Min(f, res[u, v]));
                    if (d > 0)
                    {
                        res[u, v] -= d;
                        res[v, u] += d;
                        return d;
                    }
                }
                return 0;
            }
        }
    }

    [Fact]
    public void Topological_order_respects_every_edge()
    {
        var rng = new Random(12);
        for (int trial = 0; trial < 30; trial++)
        {
            var g = trial == 0 ? Graph.Parse(Graph.DagText, directed: true, showWeights: false) : Graph.Random(rng, directed: true, showWeights: false);
            var order = GraphAlgorithms.TopologicalSort(g, new GraphTracer(g.Scene));
            Assert.Equal(g.VertexCount, order.Count);
            var pos = order.Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);
            Assert.All(g.Edges, e => Assert.True(pos[e.U] < pos[e.V]));
        }
        Assert.Equal(Graph.DagText, Graph.Parse(Graph.DagText, directed: true, showWeights: false).ToText());
        Assert.Equal(Graph.FlowText, Graph.Parse(Graph.FlowText, directed: true).ToText());
    }

    [Fact]
    public void Text_parameter_validation_fails_gracefully()
    {
        var dp = new DpSession();
        var bad = dp.Run("lcs", new Dictionary<string, int>(), new Dictionary<string, string> { ["a"] = "AB1", ["b"] = "AB" });
        Assert.True(bad.Failed);
        Assert.True(bad.Steps[^1].IsError);
        var flow = new GraphSession(Graph.Parse(Graph.FlowText, directed: true));
        Assert.True(flow.Run("maxflow", new Dictionary<string, int> { ["start"] = 2, ["sink"] = 2 }).Failed);
    }
}
