using AlgoSharp.Core.Engine;
using static AlgoSharp.Core.Engine.CellState;

namespace AlgoSharp.Core.Algorithms;

/// <summary>
/// Graph algorithms over <see cref="Graph"/>. Vertex states in the visualization:
/// Key = current vertex, Pivot = discovered / waiting, Sorted = finished, Visited = in the tree.
/// </summary>
public static class GraphAlgorithms
{
    private static string N(int v) => Graph.Name(v);

    private static EdgeKey E(int u, int v) => EdgeKey.Of(u, v);

    // <code id="graph-neighbors">
    public static List<int> Neighbors(Graph g, int u, GraphTracer T)
    {
        T.At("").Hl(Key, u).V("vertices", g.VertexCount).V("edges", g.Edges.Count).V($"adj[{N(u)}]", g.Describe(u)).Say($"An adjacency list stores, for every vertex, the list of (neighbour, weight) pairs. {N(u)} has {g.Adj[u].Count} neighbour(s).");
        var result = new List<int>(); // #list
        foreach (var (v, w) in g.Adj[u]) // #loop
        {
            result.Add(v); // #add
            T.Mark(Visited, v);
            T.MarkEdge(Active, E(u, v));
            T.At("add").Hl(Key, u).Hl(Compare, v).V("v", N(v)).V("w", w).V("result", g.Names(result)).Say($"{N(u)} — {N(v)} with weight {w}. Listing all neighbours costs O(degree), not O(V).");
        }
        T.At("ret").Hl(Key, u).V("result", g.Names(result)).Say($"Neighbours of {N(u)}: {g.Names(result)}. Total adjacency-list memory is O(V + E).");
        return result; // #ret
    }
    // </code>

    // <code id="bfs">
    public static List<int> Bfs(Graph g, int start, GraphTracer T)
    {
        var visited = new bool[g.VertexCount]; // #visited
        var order = new List<int>(); // #order
        var queue = new Queue<int>(); // #queue
        visited[start] = true; // #markStart
        queue.Enqueue(start); // #enqStart
        T.Mark(Pivot, start);
        T.At("enqStart").V("queue", g.Names(queue)).V("order", g.Names(order)).Say($"Mark {N(start)} as visited and put it in the queue.");
        while (queue.Count > 0) // #loop
        {
            int u = queue.Dequeue(); // #dequeue
            order.Add(u); // #visit
            T.At("visit").Hl(Key, u).Ptr("u", u).V("u", N(u)).V("queue", g.Names(queue)).V("order", g.Names(order)).Say($"Dequeue {N(u)} (first in, first out) and visit it.");
            foreach (var (v, _) in g.Adj[u]) // #neighbors
            {
                T.At("check").Hl(Key, u).Hl(Compare, v).HlEdge(Active, E(u, v)).Ptr("u", u).V("u", N(u)).V("v", N(v)).V("queue", g.Names(queue)).Say(visited[v] ? $"{N(v)} was already discovered: skip it." : $"{N(v)} is new.");
                if (!visited[v]) // #check
                {
                    visited[v] = true; // #markV
                    queue.Enqueue(v); // #enq
                    T.Mark(Pivot, v);
                    T.MarkEdge(Sorted, E(u, v));
                    T.At("enq").Hl(Key, u).Ptr("u", u).V("u", N(u)).V("v", N(v)).V("queue", g.Names(queue)).Say($"Mark {N(v)} visited and enqueue it. It sits one level further from {N(start)} than {N(u)}.");
                }
            }
            T.Mark(Sorted, u);
        }
        T.At("ret").V("order", g.Names(order)).Say($"Queue empty. BFS order: {g.Names(order)}. The green edges form a shortest-hop tree. O(V + E).");
        return order; // #ret
    }
    // </code>

    // <code id="dfs">
    public static List<int> Dfs(Graph g, int start, GraphTracer T)
    {
        var visited = new bool[g.VertexCount]; // #visited
        var order = new List<int>(); // #order
        Dfs(g, start, visited, order, T); // #start
        T.At("ret").V("order", g.Names(order)).Say($"DFS order: {g.Names(order)}. Each vertex and edge was handled once: O(V + E).");
        return order; // #ret
    }

    private static void Dfs(Graph g, int u, bool[] visited, List<int> order, GraphTracer T)
    {
        visited[u] = true; // #mark
        order.Add(u); // #visit
        T.Mark(Pivot, u);
        T.At("visit").Hl(Key, u).Ptr("u", u).V("u", N(u)).V("order", g.Names(order)).Say($"Visit {N(u)} and go as deep as possible before backtracking.");
        foreach (var (v, _) in g.Adj[u]) // #neighbors
        {
            T.At("check").Hl(Key, u).Hl(Compare, v).HlEdge(Active, E(u, v)).Ptr("u", u).V("u", N(u)).V("v", N(v)).Say(visited[v] ? $"{N(v)} is already visited." : $"{N(v)} is unvisited: recurse into it.");
            if (!visited[v]) // #check
            {
                T.MarkEdge(Sorted, E(u, v));
                Dfs(g, v, visited, order, T); // #recurse
                T.At("recurse").Hl(Key, u).Ptr("u", u).V("u", N(u)).V("order", g.Names(order)).Say($"Back from {N(v)}: continue with {N(u)}'s remaining neighbours.");
            }
        }
        T.Mark(Sorted, u);
        T.At("neighbors").Ptr("u", u).V("u", N(u)).V("order", g.Names(order)).Say($"All neighbours of {N(u)} are done: backtrack.");
    }
    // </code>

    // <code id="dijkstra">
    public static int[] Dijkstra(Graph g, int source, GraphTracer T)
    {
        var dist = new int[g.VertexCount]; // #dist
        var prev = new int[g.VertexCount]; // #prev
        Array.Fill(dist, int.MaxValue); // #fill
        Array.Fill(prev, -1);
        dist[source] = 0; // #source
        T.BadgeEach(g.VertexCount, x => x == source ? "0" : "∞");
        var pq = new PriorityQueue<int, int>(); // #pq
        pq.Enqueue(source, 0); // #enq
        T.Mark(Pivot, source);
        T.At("enq").V("pq", Queue(pq)).Say($"Every distance starts at ∞ except the source {N(source)} = 0. Put {N(source)} in the priority queue.");
        while (pq.TryDequeue(out int u, out int d)) // #loop
        {
            if (d > dist[u]) // #stale
            {
                T.At("skip").Hl(Visited, u).V("u", N(u)).V("d", d).V($"dist[{N(u)}]", dist[u]).Say($"Stale entry: {N(u)} was already settled with distance {dist[u]} < {d}. Skip it.");
                continue; // #skip
            }
            T.At("loop").Hl(Key, u).Ptr("u", u).V("u", N(u)).V("d", d).V("pq", Queue(pq)).Say($"Take the closest unsettled vertex: {N(u)} at distance {d}. Its distance is now final.");
            foreach (var (v, w) in g.Adj[u]) // #neighbors
            {
                T.At("relax").Hl(Key, u).Hl(Compare, v).HlEdge(Active, E(u, v)).Ptr("u", u).V("u", N(u)).V("v", N(v)).V("w", w).V($"dist[{N(u)}] + w", dist[u] + w).V($"dist[{N(v)}]", Inf(dist[v])).Say(dist[u] + w < dist[v] ? $"{dist[u]} + {w} = {dist[u] + w} < {Inf(dist[v])}: a shorter path to {N(v)}!" : $"{dist[u]} + {w} = {dist[u] + w} ≥ {Inf(dist[v])}: no improvement.");
                if (dist[u] + w < dist[v]) // #relax
                {
                    T.MarkEdge(Default, prev[v] >= 0 ? E(prev[v], v) : null);
                    dist[v] = dist[u] + w; // #update
                    prev[v] = u; // #setPrev
                    pq.Enqueue(v, dist[v]); // #push
                    T.Badge(v, dist[v].ToString());
                    T.Mark(Pivot, v);
                    T.MarkEdge(Sorted, E(u, v));
                    T.At("push").Hl(Key, u).Hl(New, v).Ptr("u", u).V("u", N(u)).V("v", N(v)).V($"dist[{N(v)}]", dist[v]).V("pq", Queue(pq)).Say($"Update dist[{N(v)}] = {dist[v]}, remember it came from {N(u)}, and push it into the queue.");
                }
            }
            T.Mark(Sorted, u);
        }
        T.At("ret").Say($"Done. Green edges form the shortest-path tree from {N(source)}. With a binary heap: O((V + E) log V).");
        return dist; // #ret
    }
    // </code>

    // <code id="kruskal">
    public static List<(int U, int V, int W)> Kruskal(Graph g, GraphTracer T)
    {
        var edges = g.Edges.OrderBy(e => e.W).ToList(); // #sort
        T.At("sort").V("edges", string.Join(" ", edges.Select(e => $"{N(e.U)}{N(e.V)}:{e.W}"))).Say("Sort all edges by weight, lightest first.");
        var parent = new int[g.VertexCount]; // #parent
        for (int i = 0; i < parent.Length; i++) parent[i] = i; // #init
        var mst = new List<(int U, int V, int W)>(); // #mst
        int total = 0; // #total
        foreach (var (u, v, w) in edges) // #loop
        {
            int ru = Find(parent, u), rv = Find(parent, v); // #find
            T.At("find").HlEdge(Compare, E(u, v)).Hl(Compare, u).Hl(Compare, v).V("edge", $"{N(u)}-{N(v)}").V("w", w).V("root(" + N(u) + ")", N(ru)).V("root(" + N(v) + ")", N(rv)).V("total", total).Say(ru != rv ? $"Edge {N(u)}-{N(v)} ({w}) joins two different components: take it." : $"{N(u)} and {N(v)} are already connected: this edge would create a cycle. Skip it.");
            T.MarkEdge(ru == rv ? Dim : Default, E(u, v));
            if (ru != rv) // #check
            {
                parent[ru] = rv; // #union
                mst.Add((u, v, w)); // #add
                total += w; // #sum
                T.MarkEdge(Sorted, E(u, v));
                T.Mark(Sorted, u);
                T.Mark(Sorted, v);
                T.At("sum").HlEdge(Sorted, E(u, v)).V("edge", $"{N(u)}-{N(v)}").V("total", total).V("mst size", mst.Count).Say($"Union the two components. MST weight so far: {total}.");
            }
            if (mst.Count == g.VertexCount - 1) break; // #full
        }
        T.At("ret").V("total", total).V("mst size", mst.Count).Say(mst.Count == g.VertexCount - 1 ? $"V − 1 = {mst.Count} edges chosen: that's a spanning tree of weight {total}. O(E log E)." : $"The graph is disconnected: this is a minimum spanning forest of weight {total}.");
        return mst; // #ret
    }

    private static int Find(int[] parent, int x)
    {
        while (parent[x] != x) x = parent[x]; // #findLoop
        return x; // #findRet
    }
    // </code>

    // <code id="prim">
    public static List<(int U, int V, int W)> Prim(Graph g, int start, GraphTracer T)
    {
        var inTree = new bool[g.VertexCount]; // #inTree
        var mst = new List<(int U, int V, int W)>(); // #mst
        var pq = new PriorityQueue<(int From, int To), int>(); // #pq
        inTree[start] = true; // #start
        foreach (var (v, w) in g.Adj[start]) pq.Enqueue((start, v), w); // #seed
        T.Mark(Sorted, start);
        T.At("seed").Hl(Key, start).V("pq", EdgeQueue(pq)).Say($"Grow the tree from {N(start)}: push all its edges into a priority queue.");
        while (pq.TryDequeue(out var e, out int w)) // #loop
        {
            T.At("loop").HlEdge(Compare, E(e.From, e.To)).Hl(Compare, e.To).V("edge", $"{N(e.From)}-{N(e.To)}").V("w", w).V("pq", EdgeQueue(pq)).Say(inTree[e.To] ? $"Cheapest edge {N(e.From)}-{N(e.To)} ({w}) leads back into the tree: skip it." : $"Cheapest edge leaving the tree: {N(e.From)}-{N(e.To)} ({w}).");
            if (inTree[e.To]) // #skip
            {
                T.MarkEdge(Dim, E(e.From, e.To));
                continue; // #continue
            }
            inTree[e.To] = true; // #take
            mst.Add((e.From, e.To, w)); // #add
            T.Mark(Sorted, e.To);
            T.MarkEdge(Sorted, E(e.From, e.To));
            T.At("add").Hl(Key, e.To).V("added", N(e.To)).V("mst weight", mst.Sum(x => x.W)).Say($"Add {N(e.To)} to the tree. Total weight: {mst.Sum(x => x.W)}.");
            foreach (var (v, w2) in g.Adj[e.To]) // #push
            {
                if (!inTree[v]) pq.Enqueue((e.To, v), w2); // #enq
            }
            T.At("enq").Hl(Key, e.To).V("pq", EdgeQueue(pq)).Say($"Push {N(e.To)}'s edges to vertices outside the tree.");
        }
        T.At("ret").V("mst weight", mst.Sum(x => x.W)).Say($"Queue empty. Prim's MST weight: {mst.Sum(x => x.W)} — the same total Kruskal finds. O(E log V).");
        return mst; // #ret
    }
    // </code>

    private static string Inf(int d) => d == int.MaxValue ? "∞" : d.ToString();

    private static string Queue(PriorityQueue<int, int> pq) =>
        "[" + string.Join(", ", pq.UnorderedItems.OrderBy(x => x.Priority).Select(x => $"{N(x.Element)}:{x.Priority}")) + "]";

    private static string EdgeQueue(PriorityQueue<(int From, int To), int> pq) =>
        "[" + string.Join(", ", pq.UnorderedItems.OrderBy(x => x.Priority).Take(6).Select(x => $"{N(x.Element.From)}{N(x.Element.To)}:{x.Priority}")) + (pq.Count > 6 ? ", …" : "") + "]";
}
