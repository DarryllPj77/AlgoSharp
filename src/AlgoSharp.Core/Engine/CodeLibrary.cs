using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;

namespace AlgoSharp.Core.Engine;

/// <summary>
/// A region of real algorithm source, as displayed in the UI. Built from the embedded .cs files
/// of this assembly: instrumentation lines (<c>T.…</c>) and tracer parameters are removed,
/// and <c>// #label</c> markers become the line numbers that steps highlight.
/// </summary>
public sealed record CodeRegion(string Id, IReadOnlyList<string> Lines, IReadOnlyDictionary<string, int> Labels)
{
    /// <summary>1-based display line for a step label, or 0 when the step has no line.</summary>
    public int LineOf(string label) => Labels.TryGetValue(label, out int line) ? line : 0;
}

public static partial class CodeLibrary
{
    private static readonly ConcurrentDictionary<string, CodeRegion> Cache = new();
    private static readonly Lazy<Dictionary<string, List<string>>> Raw = new(LoadAll);

    public static IEnumerable<string> RegionIds => Raw.Value.Keys;

    public static CodeRegion Get(string id) => Cache.GetOrAdd(id, Build);

    private static CodeRegion Build(string id)
    {
        if (!Raw.Value.TryGetValue(id, out var raw))
            throw new KeyNotFoundException($"Code region '{id}' not found.");

        var lines = new List<string>();
        var labels = new Dictionary<string, int>();
        foreach (string line in raw)
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("T.", StringComparison.Ordinal)) continue;

            string text = line;
            var m = LabelRegex().Match(text);
            if (m.Success)
            {
                labels[m.Groups[1].Value] = lines.Count + 1;
                text = text[..m.Index].TrimEnd();
            }
            text = TracerParamRegex().Replace(text, "");
            lines.Add(text);
        }

        // Dedent and trim blank edges.
        int indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        var display = lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()).ToList();
        return new CodeRegion(id, display, labels);
    }

    private static Dictionary<string, List<string>> LoadAll()
    {
        var result = new Dictionary<string, List<string>>();
        var asm = typeof(CodeLibrary).Assembly;
        foreach (string name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".cs", StringComparison.Ordinal)))
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            string? current = null;
            List<string>? buffer = null;
            while (reader.ReadLine() is { } line)
            {
                var open = RegionOpenRegex().Match(line);
                if (open.Success)
                {
                    current = open.Groups[1].Value;
                    buffer = [];
                    continue;
                }
                if (current is not null && line.Trim() == "// </code>")
                {
                    result[current] = buffer!;
                    current = null;
                    buffer = null;
                    continue;
                }
                buffer?.Add(line);
            }
        }
        return result;
    }

    [GeneratedRegex(@"\s*//\s*#([A-Za-z0-9_-]+)\s*$")]
    private static partial Regex LabelRegex();

    [GeneratedRegex(@",\s*(?:(?:ArrayTracer|ListTracer<\w+>)\s+)?T\b(?=\s*\))")]
    private static partial Regex TracerParamRegex();

    [GeneratedRegex(@"^\s*//\s*<code id=""([^""]+)"">\s*$")]
    private static partial Regex RegionOpenRegex();
}
