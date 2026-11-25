using AlgoSharp.Core.Algorithms;
using AlgoSharp.Core.Engine;

namespace AlgoSharp.Core.Catalog;

internal static class SessionHelpers
{
    public static int Arg(this IReadOnlyDictionary<string, int> args, string name) =>
        args.TryGetValue(name, out int v) ? v : 0;

    /// <summary>Runs an operation, turning a thrown exception into a failed result with its recorded steps.</summary>
    public static OperationResult Execute(Func<List<Step>> steps, Func<string> run)
    {
        try
        {
            string summary = run();
            return new OperationResult(steps(), summary, false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IndexOutOfRangeException)
        {
            return new OperationResult(steps(), $"{ex.GetType().Name}: {ex.Message}", true);
        }
    }
}

public sealed class SortSession(int[] input, Action<int[], ArrayTracer> sort) : IVisualizerSession
{
    public int[] Values { get; } = input;
    public bool Persistent => false;

    public Step Snapshot(string message) => new ArrayTracer(ArrayLayout.Bars, Values.Length, Values).Snapshot(message);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args)
    {
        int[] a = (int[])Values.Clone();
        var tracer = new ArrayTracer(ArrayLayout.Bars, a.Length, a);
        sort(a, tracer);
        int comparisons = tracer.Steps.Count(s => s.Label == "cmp");
        int moves = tracer.Steps.Count(s => s.Label is "swap" or "shift" or "takeL" or "takeR" or "restL" or "restR" or "place" or "insert");
        return new OperationResult(tracer.Steps, $"Sorted [{string.Join(", ", a)}] — {comparisons} comparisons, {moves} writes/swaps.", false);
    }
}

public sealed class SearchSession(int[] input, Func<int[], int, ArrayTracer, int> search) : IVisualizerSession
{
    public int[] Values { get; } = input;
    public bool Persistent => false;

    public Step Snapshot(string message) => new ArrayTracer(ArrayLayout.Cells, Values.Length, Values).Snapshot(message);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args)
    {
        var tracer = new ArrayTracer(ArrayLayout.Cells, Values.Length, Values);
        int target = args.Arg("target");
        int index = search((int[])Values.Clone(), target, tracer);
        int checks = tracer.Steps.Count(s => s.Label is "cmp" or "mid");
        return new OperationResult(tracer.Steps, index >= 0 ? $"Found {target} at index {index} after {checks} checks." : $"{target} not found after {checks} checks.", false);
    }
}

public sealed class ArraySession(int[] input, int capacity) : IVisualizerSession
{
    private readonly DynamicArray array = new(capacity, input);

    public int[] Values => array.ToArray();

    public Step Snapshot(string message) => array.T.Snapshot(message);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args)
    {
        array.T.BeginOperation();
        return SessionHelpers.Execute(() => array.T.Steps, () => operation switch
        {
            "access" => $"Get({args.Arg("index")}) returned {array.Get(args.Arg("index"))}.",
            "insert" => Do(() => array.Insert(args.Arg("index"), args.Arg("value")), $"Inserted {args.Arg("value")} at index {args.Arg("index")}."),
            "delete" => $"RemoveAt({args.Arg("index")}) removed {array.RemoveAt(args.Arg("index"))}.",
            "update" => Do(() => array.Set(args.Arg("index"), args.Arg("value")), $"Set index {args.Arg("index")} to {args.Arg("value")}."),
            "search" => array.IndexOf(args.Arg("value")) is var i and >= 0 ? $"IndexOf({args.Arg("value")}) = {i}." : $"{args.Arg("value")} not found (-1).",
            _ => throw new ArgumentException($"Unknown operation {operation}"),
        });
    }

    private static string Do(Action action, string summary)
    {
        action();
        return summary;
    }
}

public sealed class StackSession(int[] input, int capacity) : IVisualizerSession
{
    private readonly ArrayStack stack = new(capacity, input);

    public int[] Values => stack.ToArray();

    public Step Snapshot(string message) => stack.T.Snapshot(message);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args)
    {
        stack.T.BeginOperation();
        return SessionHelpers.Execute(() => stack.T.Steps, () => operation switch
        {
            "push" => Push(args.Arg("value")),
            "pop" => $"Pop() returned {stack.Pop()}.",
            "peek" => $"Peek() returned {stack.Peek()}.",
            _ => throw new ArgumentException($"Unknown operation {operation}"),
        });
    }

    private string Push(int value)
    {
        stack.Push(value);
        return $"Pushed {value}. Size = {stack.Count}.";
    }
}

public sealed class QueueSession(int[] input, int capacity) : IVisualizerSession
{
    private readonly ArrayQueue queue = new(capacity, input);

    public int[] Values => queue.ToArray();

    public Step Snapshot(string message) => queue.T.At("").Ptr("front", queue.Front).Ptr("rear", queue.Rear).Say(message, record: false);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args)
    {
        queue.T.BeginOperation();
        return SessionHelpers.Execute(() => queue.T.Steps, () => operation switch
        {
            "enqueue" => Enqueue(args.Arg("value")),
            "dequeue" => $"Dequeue() returned {queue.Dequeue()}.",
            "peek" => $"Peek() returned {queue.Peek()}.",
            _ => throw new ArgumentException($"Unknown operation {operation}"),
        });
    }

    private string Enqueue(int value)
    {
        queue.Enqueue(value);
        return $"Enqueued {value}. Size = {queue.Count}.";
    }
}

public sealed class LinkedListSession(int[] input) : IVisualizerSession
{
    private readonly SinglyLinkedList list = new(input);

    public int[] Values => list.ToArray();

    public Step Snapshot(string message) => list.T.Snapshot(message);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args)
    {
        list.T.BeginOperation();
        return SessionHelpers.Execute(() => list.T.Steps, () => operation switch
        {
            "insert" => Insert(args.Arg("index"), args.Arg("value")),
            "delete" => $"RemoveAt({args.Arg("index")}) removed {list.RemoveAt(args.Arg("index"))}.",
            "search" => list.IndexOf(args.Arg("value")) is var i and >= 0 ? $"IndexOf({args.Arg("value")}) = {i}." : $"{args.Arg("value")} not found (-1).",
            "traverse" => $"Sum() = {list.Sum()}.",
            _ => throw new ArgumentException($"Unknown operation {operation}"),
        });
    }

    private string Insert(int index, int value)
    {
        list.InsertAt(index, value);
        return $"Inserted {value} at position {index}.";
    }
}

public sealed class BstSession(int[] input) : IVisualizerSession
{
    private readonly BinarySearchTree tree = new(input);

    public int[] Values => [.. tree.Keys()];

    public Step Snapshot(string message) => tree.T.Snapshot(message);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args)
    {
        tree.T.BeginOperation();
        int value = args.Arg("value");
        return SessionHelpers.Execute(() => tree.T.Steps, () => operation switch
        {
            "insert" when tree.Count >= 15 && !tree.Keys().Contains(value) => throw new InvalidOperationException("This visualizer keeps trees at 15 keys or fewer."),
            "insert" => tree.Insert(value) ? $"Inserted {value}. Height = {tree.Height()}." : $"{value} was already present.",
            "search" => tree.Contains(value) ? $"Found {value}." : $"{value} not found.",
            "delete" => Delete(value),
            "inorder" => $"In-order: [{string.Join(", ", tree.InOrder())}].",
            _ => throw new ArgumentException($"Unknown operation {operation}"),
        });
    }

    private string Delete(int value)
    {
        bool had = tree.Keys().Contains(value);
        tree.Delete(value);
        return had ? $"Deleted {value}. Height = {tree.Height()}." : $"{value} was not in the tree.";
    }
}

public sealed class HeapSession(int[] input, int capacity) : IVisualizerSession
{
    private readonly MinHeap heap = new(capacity, input);

    public int[] Values => heap.ToArray();

    public Step Snapshot(string message) => WithTree(heap.T.Snapshot(message));

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args)
    {
        heap.T.BeginOperation();
        var result = SessionHelpers.Execute(() => heap.T.Steps, () => operation switch
        {
            "insert" => Insert(args.Arg("value")),
            "extract" => $"ExtractMin() returned {heap.ExtractMin()}.",
            "peek" => $"Peek() returned {heap.Peek()}.",
            _ => throw new ArgumentException($"Unknown operation {operation}"),
        });
        return result with { Steps = result.Steps.Select(WithTree).ToList() };
    }

    private string Insert(int value)
    {
        heap.Insert(value);
        return $"Inserted {value}. Size = {heap.Count}.";
    }

    /// <summary>The array is the real storage; the tree view is derived from the same cells.</summary>
    private static Step WithTree(Step s) => s.Array is null ? s : s with { Graph = TreeLayout.HeapTree(s.Array) };
}

public sealed class HashSession(int[] input, int buckets) : IVisualizerSession
{
    private readonly HashTable table = new(buckets, input);

    public int[] Values => Enumerable.Range(0, table.BucketCount).SelectMany(b => table.Chain(b).AsEnumerable().Reverse()).ToArray();

    public Step Snapshot(string message) => table.T.Snapshot(message);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args)
    {
        table.T.BeginOperation();
        int key = args.Arg("value");
        return SessionHelpers.Execute(() => table.T.Steps, () => operation switch
        {
            "insert" when table.Count >= 30 => throw new InvalidOperationException("This visualizer keeps at most 30 keys."),
            "insert" => table.Insert(key) ? $"Inserted {key}. Load factor = {(double)table.Count / table.BucketCount:0.00}." : $"{key} was already present.",
            "search" => table.Contains(key) ? $"Found {key}." : $"{key} not found.",
            "delete" => table.Remove(key) ? $"Removed {key}." : $"{key} was not present.",
            _ => throw new ArgumentException($"Unknown operation {operation}"),
        });
    }
}

public sealed class GraphSession(Graph graph) : IVisualizerSession
{
    public int[] Values => [];
    public bool Persistent => false;
    public string Text => graph.ToText();
    public IReadOnlyList<string> Vertices => Enumerable.Range(0, graph.VertexCount).Select(Graph.Name).ToList();

    public Step Snapshot(string message) => new GraphTracer(graph.Scene).Snapshot(message);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args)
    {
        var tracer = new GraphTracer(graph.Scene);
        int start = Math.Clamp(args.Arg("start"), 0, graph.VertexCount - 1);
        string summary;
        try
        {
            summary = Dispatch(operation, args, tracer, start);
        }
        catch (InvalidOperationException ex)
        {
            tracer.At("").Error(ex.Message);
            return new OperationResult(tracer.Steps, ex.Message, true);
        }
        return new OperationResult(tracer.Steps, summary, false);
    }

    private string Dispatch(string operation, IReadOnlyDictionary<string, int> args, GraphTracer tracer, int start)
    {
        return operation switch
        {
            "neighbors" => $"Neighbors({Graph.Name(start)}) = {graph.Names(GraphAlgorithms.Neighbors(graph, start, tracer))}.",
            "bfs" => $"BFS from {Graph.Name(start)}: {graph.Names(GraphAlgorithms.Bfs(graph, start, tracer))}.",
            "dfs" => $"DFS from {Graph.Name(start)}: {graph.Names(GraphAlgorithms.Dfs(graph, start, tracer))}.",
            "dijkstra" => Distances(GraphAlgorithms.Dijkstra(graph, start, tracer)),
            "kruskal" => $"Kruskal MST weight = {GraphAlgorithms.Kruskal(graph, tracer).Sum(e => e.W)}.",
            "prim" => $"Prim MST weight = {GraphAlgorithms.Prim(graph, start, tracer).Sum(e => e.W)}.",
            "maxflow" when start == Math.Clamp(args.Arg("sink"), 0, graph.VertexCount - 1) => throw new InvalidOperationException("Source and sink must differ."),
            "maxflow" => $"Max flow {Graph.Name(start)} → {Graph.Name(Math.Clamp(args.Arg("sink"), 0, graph.VertexCount - 1))} = {GraphAlgorithms.MaxFlow(graph, start, Math.Clamp(args.Arg("sink"), 0, graph.VertexCount - 1), tracer)}.",
            "topo" => $"Topological order: {graph.Names(GraphAlgorithms.TopologicalSort(graph, tracer))}.",
            _ => throw new ArgumentException($"Unknown operation {operation}"),
        };
    }

    private static string Distances(int[] dist) =>
        "Distances: " + string.Join(", ", dist.Select((d, v) => $"{Graph.Name(v)}={(d == int.MaxValue ? "∞" : d.ToString())}"));
}

public sealed class RecursionSession : IVisualizerSession
{
    public int[] Values => [];
    public bool Persistent => false;

    public Step Snapshot(string message) => new CallTreeTracer().Snapshot(message);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args)
    {
        var tracer = new CallTreeTracer();
        int n = args.Arg("n");
        string summary = operation switch
        {
            "fib" => $"fib({n}) = {Recursion.Fib(n, tracer)} using {tracer.Calls} calls.",
            "fib-memo" => $"fib({n}) = {Recursion.FibMemo(n, tracer)} using only {tracer.Calls} calls.",
            "factorial" => $"{n}! = {Recursion.Factorial(n, tracer)} using {tracer.Calls} calls.",
            _ => throw new ArgumentException($"Unknown operation {operation}"),
        };
        return new OperationResult(tracer.Steps, summary, false);
    }
}

internal static class TextArgs
{
    public static string Text(this IReadOnlyDictionary<string, string> texts, string name) =>
        texts.TryGetValue(name, out var v) ? v.Trim() : "";

    public static string Word(this IReadOnlyDictionary<string, string> texts, string name, int maxLength, bool upper = false)
    {
        string w = texts.Text(name);
        w = upper ? w.ToUpperInvariant() : w.ToLowerInvariant();
        if (w.Length == 0) throw new InvalidOperationException($"Enter a value for {name}.");
        if (w.Length > maxLength) throw new InvalidOperationException($"Keep {name} to {maxLength} letters or fewer.");
        if (!w.All(c => upper ? c is >= 'A' and <= 'Z' : c is >= 'a' and <= 'z'))
            throw new InvalidOperationException($"Use only letters {(upper ? "A–Z" : "a–z")} in {name}.");
        return w;
    }
}

public sealed class SegmentTreeSession(int[] input) : IVisualizerSession
{
    private readonly SegmentTree tree = new(input);

    public int[] Values => tree.Values;

    public Step Snapshot(string message) => tree.T.Snapshot(message);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args)
    {
        tree.T.BeginOperation();
        var result = SessionHelpers.Execute(() => tree.T.Steps, () =>
        {
            int n = tree.Length;
            switch (operation)
            {
                case "build":
                    tree.Build();
                    return $"Built a tree of {2 * n - 1} nodes. Total = {tree.Root}.";
                case "query":
                    int l = args.Arg("l"), r = args.Arg("r");
                    if (l < 0 || r >= n || l > r) throw new InvalidOperationException($"Choose 0 ≤ l ≤ r ≤ {n - 1}.");
                    int sum = tree.Query(l, r);
                    tree.T.At("start").V("sum", sum).Say($"Sum of [{l}..{r}] = {sum}. Green nodes were used whole; at most ~4 nodes per level are touched: O(log n).");
                    return $"Query({l}, {r}) = {sum}.";
                case "update":
                    int i = args.Arg("index"), v = args.Arg("value");
                    if (i < 0 || i >= n) throw new InvalidOperationException($"Index must be 0..{n - 1}.");
                    tree.Update(i, v);
                    tree.T.At("start").V("root", tree.Root).Say($"Updated index {i} to {v}. New total: {tree.Root}.");
                    return $"Update({i}, {v}). Total = {tree.Root}.";
                default:
                    throw new ArgumentException($"Unknown operation {operation}");
            }
        });
        tree.T.RevealAll();
        return result;
    }
}

public sealed class UnionFindSession : IVisualizerSession
{
    private readonly UnionFind sets = new(10);

    public UnionFindSession()
    {
        foreach (var (a, b) in new[] { (0, 1), (2, 3), (1, 3), (5, 6), (7, 8) }) sets.Union(a, b);
        sets.T.BeginOperation();
    }

    public int[] Values => sets.Parents;

    public Step Snapshot(string message) => sets.T.Snapshot(message);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args)
    {
        sets.T.BeginOperation();
        int a = args.Arg("a"), b = args.Arg("b");
        return SessionHelpers.Execute(() => sets.T.Steps, () => operation switch
        {
            "union" => sets.Union(a, b) ? $"Union({a}, {b}) merged two sets. {sets.Sets} set(s) left." : $"{a} and {b} were already together.",
            "find" => $"Find({a}) = {sets.Find(a)}.",
            "connected" => sets.Connected(a, b) ? $"{a} and {b} are connected." : $"{a} and {b} are not connected.",
            _ => throw new ArgumentException($"Unknown operation {operation}"),
        });
    }
}

public sealed class TrieSession(IEnumerable<string> words) : IVisualizerSession
{
    private readonly Trie trie = new(words);

    public int[] Values => [];
    public string Text => string.Join(", ", trie.Words());

    public Step Snapshot(string message) => trie.T.Snapshot(message);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args) => Run(operation, args, new Dictionary<string, string>());

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args, IReadOnlyDictionary<string, string> texts)
    {
        trie.T.BeginOperation();
        return SessionHelpers.Execute(() => trie.T.Steps, () =>
        {
            string w = texts.Word("word", 8);
            switch (operation)
            {
                case "insert":
                    if (trie.Words().Count >= 12 && !trie.Words().Contains(w)) throw new InvalidOperationException("This visualizer keeps at most 12 words.");
                    trie.Insert(w);
                    return $"Inserted \"{w}\". {trie.NodeCount()} nodes store {trie.Words().Count} words.";
                case "search":
                    return trie.Contains(w) ? $"\"{w}\" is stored." : $"\"{w}\" is not stored.";
                case "prefix":
                    return trie.StartsWith(w) ? $"Some word starts with \"{w}\"." : $"No word starts with \"{w}\".";
                default:
                    throw new ArgumentException($"Unknown operation {operation}");
            }
        });
    }

    public static TrieSession Parse(string text)
    {
        var words = text.Split([',', ' ', ';', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim().ToLowerInvariant()).ToList();
        if (words.Count > 12) throw new FormatException("Use at most 12 words.");
        foreach (var w in words)
        {
            if (w.Length > 8 || !w.All(c => c is >= 'a' and <= 'z')) throw new FormatException($"“{w}”: use words of up to 8 letters a–z.");
        }
        return new TrieSession(words);
    }
}

public sealed class DpSession : IVisualizerSession
{
    public int[] Values => [];
    public bool Persistent => false;

    public Step Snapshot(string message) => new TableTracer([""], [""]).Snapshot(message);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args) => Run(operation, args, new Dictionary<string, string>());

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args, IReadOnlyDictionary<string, string> texts)
    {
        try
        {
            switch (operation)
            {
                case "lcs":
                {
                    string a = texts.Word("a", 8, upper: true), b = texts.Word("b", 8, upper: true);
                    var t = DynamicProgramming.LcsTable(a, b);
                    string lcs = DynamicProgramming.Lcs(a, b, t);
                    return new OperationResult(t.Steps, $"LCS(\"{a}\", \"{b}\") = \"{lcs}\" (length {lcs.Length}).", false);
                }
                case "coins":
                {
                    int amount = Math.Clamp(args.Arg("amount"), 1, 20);
                    var coins = texts.Text("coins").Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
                        .Select(c => int.TryParse(c, out int v) && v is >= 1 and <= 20 ? v : throw new InvalidOperationException($"“{c}” is not a coin between 1 and 20."))
                        .Distinct().Order().ToArray();
                    if (coins.Length is 0 or > 6) throw new InvalidOperationException("Enter 1–6 coin values, e.g. 1, 3, 4.");
                    var t = DynamicProgramming.CoinTable(amount);
                    int result = DynamicProgramming.MinCoins(coins, amount, t);
                    return new OperationResult(t.Steps, result < 0 ? $"{amount} can't be made." : $"MinCoins = {result}.", false);
                }
                case "fib":
                {
                    int n = Math.Clamp(args.Arg("n"), 1, 15);
                    var t = DynamicProgramming.FibTable(n);
                    return new OperationResult(t.Steps, $"fib({n}) = {DynamicProgramming.Fib(n, t)}.", false);
                }
                default:
                    throw new ArgumentException($"Unknown operation {operation}");
            }
        }
        catch (InvalidOperationException ex)
        {
            var t = new TableTracer([""], [""]);
            t.At("").Say(ex.Message);
            return new OperationResult([t.Steps[0] with { IsError = true }], ex.Message, true);
        }
    }
}
