using AlgoSharp.Core.Algorithms;
using AlgoSharp.Core.Catalog;
using AlgoSharp.Core.Engine;

namespace AlgoSharp.Core.Tests;

public class TreeTests
{
    [Fact]
    public void Bst_matches_sorted_set()
    {
        var rng = new Random(5);
        var reference = new SortedSet<int>();
        var tree = new BinarySearchTree([]);
        for (int i = 0; i < 400; i++)
        {
            tree.T.BeginOperation();
            int v = rng.Next(40);
            switch (rng.Next(3))
            {
                case 0: Assert.Equal(reference.Add(v), tree.Insert(v)); break;
                case 1: tree.Delete(v); reference.Remove(v); break;
                default: Assert.Equal(reference.Contains(v), tree.Contains(v)); break;
            }
            Assert.Equal(reference, tree.Keys());
        }
        tree.T.BeginOperation();
        Assert.Equal(reference, tree.InOrder());
    }

    [Fact]
    public void Bst_frame_draws_one_node_per_key_with_parent_edges()
    {
        var tree = new BinarySearchTree([50, 30, 70, 20, 40]);
        var frame = tree.T.Snapshot("").Graph!;
        Assert.Equal(5, frame.Nodes.Count);
        Assert.Equal(4, frame.Edges.Count);
        // In-order rank gives x order.
        Assert.Equal(["20", "30", "40", "50", "70"], frame.Nodes.OrderBy(n => n.X).Select(n => n.Label));
    }

    [Fact]
    public void Heap_matches_priority_queue_and_tree_view()
    {
        var rng = new Random(9);
        var heap = new MinHeap(15, []);
        var reference = new PriorityQueue<int, int>();
        for (int i = 0; i < 300; i++)
        {
            heap.T.BeginOperation();
            if (rng.Next(2) == 0 && reference.Count < 15)
            {
                int v = rng.Next(100);
                heap.Insert(v);
                reference.Enqueue(v, v);
            }
            else if (reference.Count > 0)
            {
                Assert.Equal(reference.Dequeue(), heap.ExtractMin());
            }
            var a = heap.ToArray();
            for (int k = 1; k < a.Length; k++) Assert.True(a[(k - 1) / 2] <= a[k]);
            Assert.Equal(a, heap.T.Slots().Take(a.Length).Select(s => s!.Value));
        }
        var session = new HeapSession([5, 3, 8], 15);
        var step = session.Run("insert", new Dictionary<string, int> { ["value"] = 1 }).Steps[^1];
        Assert.Equal(4, step.Graph!.Nodes.Count);
        Assert.Equal(3, step.Graph.Edges.Count);
    }

    [Fact]
    public void Hash_table_matches_hash_set()
    {
        var rng = new Random(13);
        var table = new HashTable(7, []);
        var reference = new HashSet<int>();
        for (int i = 0; i < 300; i++)
        {
            table.T.BeginOperation();
            int v = rng.Next(60);
            switch (rng.Next(3))
            {
                case 0: Assert.Equal(reference.Add(v), table.Insert(v)); break;
                case 1: Assert.Equal(reference.Remove(v), table.Remove(v)); break;
                default: Assert.Equal(reference.Contains(v), table.Contains(v)); break;
            }
            Assert.Equal(reference.Count, table.Count);
            for (int b = 0; b < 7; b++) Assert.All(table.Chain(b), k => Assert.Equal(b, k % 7));
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(6, 8)]
    public void Recursion_results_and_call_counts(int n, int fib)
    {
        var naive = new CallTreeTracer();
        Assert.Equal(fib, Recursion.Fib(n, naive));
        var memo = new CallTreeTracer();
        Assert.Equal(fib, Recursion.FibMemo(n, memo));
        Assert.True(memo.Calls <= naive.Calls);
        if (n == 6)
        {
            Assert.Equal(25, naive.Calls);
            Assert.Equal(11, memo.Calls);
            Assert.Equal(25, naive.Steps[^1].Graph!.Nodes.Count);
        }
        Assert.Equal(120, Recursion.Factorial(5, new CallTreeTracer()));
    }
}

public class GraphTests
{
    private static Graph G => Graph.Parse(Graph.DefaultText);

    [Fact]
    public void Parse_round_trips_and_rejects_bad_input()
    {
        Assert.Equal(Graph.DefaultText, G.ToText());
        Assert.Throws<FormatException>(() => Graph.Parse("A-Z:3"));
        Assert.Throws<FormatException>(() => Graph.Parse("A-A:3"));
        Assert.Throws<FormatException>(() => Graph.Parse("hello"));
        Assert.Equal(2, Graph.Parse("a-b").VertexCount);
    }

    [Fact]
    public void Bfs_and_dfs_visit_every_reachable_vertex_once()
    {
        var bfs = GraphAlgorithms.Bfs(G, 0, new GraphTracer(G.Scene));
        var dfs = GraphAlgorithms.Dfs(G, 0, new GraphTracer(G.Scene));
        Assert.Equal(8, bfs.Distinct().Count());
        Assert.Equal(8, dfs.Distinct().Count());
        Assert.Equal([0, 1, 2, 3, 7], bfs.Take(5));   // A, then its neighbours B C, then B's D H
        Assert.Equal([0, 1, 2, 3, 4], dfs.Take(5));   // A → B → C → D → E
    }

    [Fact]
    public void Dijkstra_matches_bellman_ford()
    {
        var rng = new Random(21);
        for (int trial = 0; trial < 40; trial++)
        {
            var g = trial == 0 ? G : Graph.Random(rng);
            for (int s = 0; s < g.VertexCount; s++)
            {
                var dist = GraphAlgorithms.Dijkstra(g, s, new GraphTracer(g.Scene));
                var bf = new long[g.VertexCount];
                Array.Fill(bf, long.MaxValue);
                bf[s] = 0;
                for (int k = 0; k < g.VertexCount; k++)
                {
                    foreach (var (u, v, w) in g.Edges)
                    {
                        if (bf[u] != long.MaxValue && bf[u] + w < bf[v]) bf[v] = bf[u] + w;
                        if (bf[v] != long.MaxValue && bf[v] + w < bf[u]) bf[u] = bf[v] + w;
                    }
                }
                Assert.Equal(bf.Select(d => d == long.MaxValue ? int.MaxValue : (int)d), dist);
            }
        }
    }

    [Fact]
    public void Kruskal_and_prim_agree_on_weight()
    {
        var rng = new Random(4);
        for (int trial = 0; trial < 40; trial++)
        {
            var g = trial == 0 ? G : Graph.Random(rng);
            var k = GraphAlgorithms.Kruskal(g, new GraphTracer(g.Scene));
            var p = GraphAlgorithms.Prim(g, 0, new GraphTracer(g.Scene));
            Assert.Equal(g.VertexCount - 1, k.Count);
            Assert.Equal(k.Sum(e => e.W), p.Sum(e => e.W));
        }
    }
}

public class Phase2CodeSyncTests
{
    [Fact]
    public void Every_phase2_step_points_at_a_displayed_line()
    {
        var args = new Dictionary<string, int> { ["value"] = 30, ["start"] = 0, ["n"] = 4 };
        foreach (var def in AlgorithmCatalog.All.Where(a => a.Available && a.Phase == 2))
        {
            foreach (var op in def.Operations)
            {
                var region = CodeLibrary.Get(op.CodeId);
                var session = def.CreateFromText is { } fromText ? fromText(def.Input.DefaultText) : def.CreateSession!(def.Input.Default);
                var result = session.Run(op.Key, args);
                Assert.NotEmpty(result.Steps);
                Assert.All(result.Steps, s => Assert.NotNull(s.Graph));
                foreach (var step in result.Steps.Where(s => s.Label != ""))
                {
                    Assert.True(region.LineOf(step.Label) > 0, $"{def.Slug}/{op.Key}: label '{step.Label}' has no line in '{op.CodeId}'");
                }
            }
        }
    }
}
