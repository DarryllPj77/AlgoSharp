using System.Text.RegularExpressions;
using AlgoSharp.Core.Engine;

namespace AlgoSharp.Core.Algorithms;

/// <summary>Undirected weighted graph stored as adjacency lists. Vertices are named A–H.</summary>
public sealed partial class Graph
{
    public const int MaxVertices = 8;

    public const string DefaultText = "A-B:4, A-C:2, B-C:5, B-D:10, C-D:3, C-F:8, D-E:6, D-F:2, E-F:3, E-G:4, F-G:7, B-H:1, E-H:9, D-H:5";

    /// <summary>Fixed positions so the same vertex always appears in the same place.</summary>
    private static readonly (double X, double Y)[] Layout =
        [(8, 30), (28, 12), (28, 50), (50, 31), (72, 12), (72, 50), (92, 30), (50, 5)];

    /// <summary>Edges that never cross another candidate when drawn with <see cref="Layout"/>.</summary>
    private static readonly (int, int)[] Candidates =
        [(0, 1), (0, 2), (1, 2), (1, 3), (2, 3), (2, 5), (3, 4), (3, 5), (4, 5), (4, 6), (5, 6), (1, 7), (7, 4), (7, 3)];

    public Graph(int vertexCount, IEnumerable<(int U, int V, int W)> edges)
    {
        VertexCount = vertexCount;
        Adj = new List<(int To, int Weight)>[vertexCount];
        for (int i = 0; i < vertexCount; i++) Adj[i] = [];
        foreach (var (u, v, w) in edges)
        {
            if (u == v || Adj[u].Any(e => e.To == v)) continue;
            Adj[u].Add((v, w));
            Adj[v].Add((u, w));
            Edges.Add((Math.Min(u, v), Math.Max(u, v), w));
        }
        foreach (var list in Adj) list.Sort((a, b) => a.To.CompareTo(b.To));
    }

    public int VertexCount { get; }
    public List<(int To, int Weight)>[] Adj { get; }
    public List<(int U, int V, int W)> Edges { get; } = [];

    public static string Name(int v) => ((char)('A' + v)).ToString();

    public string Names(IEnumerable<int> vertices) => "[" + string.Join(", ", vertices.Select(Name)) + "]";

    public string Describe(int v) => string.Join(", ", Adj[v].Select(e => $"{Name(e.To)}({e.Weight})"));

    public void Scene(SceneBuilder sb)
    {
        for (int v = 0; v < VertexCount; v++) sb.Node(v, Name(v), Layout[v].X, Layout[v].Y);
        foreach (var (u, v, w) in Edges) sb.Edge(EdgeKey.Of(u, v), u, v, w.ToString());
        sb.Height = 57;
    }

    public string ToText() => string.Join(", ", Edges.Select(e => $"{Name(e.U)}-{Name(e.V)}:{e.W}"));

    /// <summary>Parses "A-B:4, B-C:2". Weight defaults to 1. Throws <see cref="FormatException"/> with a readable message.</summary>
    public static Graph Parse(string text)
    {
        var edges = new List<(int, int, int)>();
        int maxVertex = 0;
        foreach (string raw in text.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = EdgeRegex().Match(raw);
            if (!m.Success) throw new FormatException($"“{raw}” should look like A-B:4.");
            int u = char.ToUpperInvariant(m.Groups[1].Value[0]) - 'A';
            int v = char.ToUpperInvariant(m.Groups[2].Value[0]) - 'A';
            if (u >= MaxVertices || v >= MaxVertices) throw new FormatException($"Use vertices A–{Name(MaxVertices - 1)} only.");
            if (u == v) throw new FormatException($"“{raw}” is a self-loop.");
            int w = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 1;
            if (w is < 1 or > 99) throw new FormatException("Weights must be between 1 and 99.");
            edges.Add((u, v, w));
            maxVertex = Math.Max(maxVertex, Math.Max(u, v));
        }
        if (edges.Count == 0) throw new FormatException("Enter at least one edge, e.g. A-B:4.");
        return new Graph(maxVertex + 1, edges);
    }

    /// <summary>A random connected graph on all 8 vertices, using only non-crossing candidate edges.</summary>
    public static Graph Random(Random rng)
    {
        var shuffled = Candidates.OrderBy(_ => rng.Next()).ToList();
        var parent = Enumerable.Range(0, MaxVertices).ToArray();
        int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
        var chosen = new List<(int, int, int)>();
        foreach (var (u, v) in shuffled)
        {
            int ru = Find(u), rv = Find(v);
            bool bridge = ru != rv;
            if (bridge) parent[ru] = rv;
            if (bridge || rng.NextDouble() < 0.45) chosen.Add((u, v, rng.Next(1, 10)));
        }
        return new Graph(MaxVertices, chosen);
    }

    [GeneratedRegex(@"^([A-Za-z])\s*-\s*([A-Za-z])\s*(?::\s*(\d+))?$")]
    private static partial Regex EdgeRegex();
}
