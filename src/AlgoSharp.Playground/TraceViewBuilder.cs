using AlgoSharp.Core.Engine;
using AlgoSharp.Tracing;

namespace AlgoSharp.Playground;

/// <summary>A visual for one container variable at one step.</summary>
public sealed record ContainerView(string Title, ArrayFrame? Array, MapFrame? Map, GraphFrame? Graph = null);

/// <summary>One replayable frame: the step (line, message, variables) plus its container visuals.</summary>
public sealed record TraceFrame(Step Step, int Line, IReadOnlyList<ContainerView> Containers, TraceEventKind Kind);

/// <summary>
/// Turns a recorded trace into frames for the existing visual components. Everything shown
/// comes from the trace: values, which elements were read, which keys were looked up, and
/// which variables were used as indices. Nothing is inferred from the algorithm's identity.
/// </summary>
public static class TraceViewBuilder
{
    private static readonly HashSet<string> SequenceKinds = ["array", "list", "stack", "queue", "heap"];
    private static readonly HashSet<string> MapKinds = ["dict", "set"];
    private static readonly HashSet<string> NodeKinds = ["listnode", "treenode"];

    public static List<TraceFrame> Build(CaseResult result, IReadOnlyList<string> sourceLines)
    {
        var frames = new List<TraceFrame>();
        var previousValues = new Dictionary<int, TraceValue>();
        var indexNames = new Dictionary<int, List<string>>();
        var lastScalars = new Dictionary<(int Depth, string Name), string>();
        var indexedRefs = new HashSet<int>();
        var previousLinks = new Dictionary<int, (int, int)>();
        long conditions = 0, reads = 0, lookups = 0;

        foreach (var e in result.Events)
        {
            conditions += e.Kind == TraceEventKind.Condition ? 1 : 0;
            reads += e.Accesses.Count(a => a.Kind == "read");
            lookups += e.Accesses.Count(a => a.Kind == "probe");
            foreach (var a in e.Accesses) indexedRefs.Add(a.Ref);
            foreach (var a in e.Accesses.Where(a => a.IndexName is not null))
            {
                var names = indexNames.TryGetValue(a.Ref, out var l) ? l : indexNames[a.Ref] = [];
                if (!names.Contains(a.IndexName!)) names.Add(a.IndexName!);
            }

            var scalars = e.Vars.Where(v => !SequenceKinds.Contains(v.Value.Kind) && !MapKinds.Contains(v.Value.Kind) && !NodeKinds.Contains(v.Value.Kind)).ToList();
            var changes = scalars
                .Where(v => !lastScalars.TryGetValue((e.Depth, v.Name), out var old) || old != v.Value.Text)
                .Select(v => $"{v.Name} = {v.Value.Text}")
                .ToList();
            foreach (var v in scalars) lastScalars[(e.Depth, v.Name)] = v.Value.Text ?? "";

            var containers = new List<ContainerView>();
            foreach (var group in e.Vars.Where(v => v.Value.Ref != 0 && (SequenceKinds.Contains(v.Value.Kind) || MapKinds.Contains(v.Value.Kind))).GroupBy(v => v.Value.Ref))
            {
                var value = group.First().Value;
                string title = string.Join(" = ", group.Select(v => v.Name)) + "  ·  " + value.Type;
                previousValues.TryGetValue(value.Ref, out var before);
                var accesses = e.Accesses.Where(a => a.Ref == value.Ref).ToList();
                if (SequenceKinds.Contains(value.Kind))
                {
                    containers.Add(new ContainerView(title, ToArray(value, before, accesses, scalars, indexNames.GetValueOrDefault(value.Ref)), null));
                }
                else
                {
                    containers.Add(new ContainerView(title, null, ToMap(value, before, accesses)));
                }
                previousValues[value.Ref] = value;
            }

            // Strings the code indexes into are shown as character cells too.
            foreach (var group in scalars.Where(v => v.Value is { Type: "string", Ref: > 0 } && indexedRefs.Contains(v.Value.Ref)).GroupBy(v => v.Value.Ref))
            {
                var value = group.First().Value;
                string text = value.Text is { Length: >= 2 } t ? t[1..^1] : "";
                var asChars = value with { Kind = "array", Items = text.Select(c => "'" + c + "'").ToList() };
                previousValues.TryGetValue(value.Ref, out var before);
                containers.Insert(0, new ContainerView(string.Join(" = ", group.Select(v => v.Name)) + "  ·  string",
                    ToArray(asChars, before, e.Accesses.Where(a => a.Ref == value.Ref).ToList(), scalars, indexNames.GetValueOrDefault(value.Ref)), null));
                previousValues[value.Ref] = asChars;
            }

            var nodeVars = e.Vars.Where(v => NodeKinds.Contains(v.Value.Kind)).ToList();
            var nullPointers = e.Vars.Where(v => v.Value.Kind == "null").Select(v => v.Name).ToList();
            if (nodeVars.Any(v => v.Value.Kind == "listnode")) containers.Add(new ContainerView("linked list", null, null, ListGraph(nodeVars.Where(v => v.Value.Kind == "listnode").ToList(), previousLinks)));
            foreach (var (title, graph) in TreeGraphs(nodeVars.Where(v => v.Value.Kind == "treenode").ToList(), previousLinks)) containers.Add(new ContainerView(title, null, null, graph));
            foreach (var n in nodeVars.SelectMany(v => v.Value.Nodes ?? [])) previousLinks[n.Ref] = (n.Next, n.Right);

            var vars = scalars.Select(v => new VarView(v.Name, v.Value.Text ?? v.Value.Kind)).ToList();
            vars.AddRange(nodeVars.Select(v => new VarView(v.Name, $"→ node {v.Value.Text}")));
            if (e.Depth > 1) vars.Insert(0, new VarView("call depth", $"{e.Method} #{e.Depth}"));

            var step = new Step(e.Line.ToString(), Describe(e, sourceLines, changes, result), vars, IsError: e.Kind == TraceEventKind.Exception)
            {
                Callout = CalloutFor(e),
                Counters =
                [
                    new VarView("condition checks", conditions.ToString()),
                    new VarView("element reads", reads.ToString()),
                    new VarView("lookups", lookups.ToString()),
                ],
            };
            frames.Add(new TraceFrame(step, e.Line, containers, e.Kind));
        }
        return frames;
    }

    private static Callout? CalloutFor(TraceEvent e) => e.Kind switch
    {
        TraceEventKind.Condition => new Callout($"{e.Text}  →  {(e.Result == true ? "true" : "false")}", e.Result == true ? CellState.Found : CellState.Compare),
        TraceEventKind.Return => new Callout($"return {e.Text}", CellState.New),
        TraceEventKind.Exception => new Callout(e.Text ?? "Exception", CellState.Error),
        _ => null,
    };

    private static string Describe(TraceEvent e, IReadOnlyList<string> source, List<string> changes, CaseResult result)
    {
        string code = e.Line >= 1 && e.Line <= source.Count ? source[e.Line - 1].Trim() : "";
        string changed = changes.Count == 0 ? "" : " Now " + string.Join(", ", changes) + ".";
        string accessed = string.Join(" ", e.Accesses.Take(4).Select(a => a.Kind == "probe"
            ? $"Looked up key {a.Key}: {(a.Hit == true ? "found" : a.Hit == false ? "not found" : "checked")}."
            : $"Read element [{a.Key}]."));
        return e.Kind switch
        {
            TraceEventKind.Condition when IsLoop(code) => $"Loop condition {e.Text} is {(e.Result == true ? "true, so the loop body runs again" : "false, so the loop ends")}.{changed} {accessed}".Trim(),
            TraceEventKind.Condition => $"Condition {e.Text} is {(e.Result == true ? "true, so the if-block runs" : "false, so the if-block is skipped")}. {accessed}".Trim(),
            TraceEventKind.Enter => $"Called {e.Method}.{changed}",
            TraceEventKind.Return => $"Line {e.Line} returns {e.Text}. {accessed}".Trim(),
            TraceEventKind.Exception => $"{e.Text} on line {e.Line}. The variables below are their values when it happened; step back to see how they got there.",
            _ => $"Line {e.Line}: {code}{changed} {accessed}".Trim(),
        };
    }

    private static bool IsLoop(string code) =>
        code.StartsWith("for", StringComparison.Ordinal) || code.StartsWith("while", StringComparison.Ordinal) || code.StartsWith("}", StringComparison.Ordinal) || code.StartsWith("do", StringComparison.Ordinal);

    private static ArrayFrame ToArray(TraceValue value, TraceValue? before, List<TraceAccess> accesses, List<TraceVar> scalars, List<string>? indexVars)
    {
        var items = value.Items ?? [];
        var reads = accesses.Where(a => a.Kind == "read").Select(a => a.Key).ToHashSet();
        var probes = accesses.Where(a => a.Kind == "probe").Select(a => a.Key).ToHashSet();
        var cells = new List<CellView>();
        for (int i = 0; i < items.Count; i++)
        {
            bool changed = before?.Items is { } old && (i >= old.Count || old[i] != items[i]);
            var state = reads.Contains(i.ToString()) ? CellState.Compare
                : changed ? CellState.New
                : probes.Contains(items[i]) ? CellState.Key
                : CellState.Default;
            int.TryParse(items[i], out int number);
            cells.Add(new CellView(i, number, i, 0, state) { Label = items[i] });
        }

        // Pointers: only variables the code actually used to index this container, at their current value.
        var pointers = new List<PointerView>();
        foreach (var name in indexVars ?? [])
        {
            var v = scalars.FirstOrDefault(s => s.Name == name);
            if (v is null || !int.TryParse(v.Value.Text, out int pos) || pos < 0 || pos >= items.Count) continue;
            pointers.Add(new PointerView(name, pos, 0, pointers.Count(p => p.Pos == pos)));
        }

        if (value.Kind == "stack")
        {
            // Stack<T> enumerates from the top; draw it bottom-up.
            cells = cells.Select(c => c with { Pos = items.Count - 1 - c.Pos, Id = items.Count - 1 - c.Id }).ToList();
            pointers.Clear();
        }
        var layout = value.Kind == "stack" ? ArrayLayout.Stack : ArrayLayout.Cells;
        return new ArrayFrame(layout, Math.Max(1, items.Count), cells, pointers, -1, -1, false, 1);
    }

    // ------------------------------------------------------------------ linked structures

    /// <summary>All list nodes reachable from list variables, laid out chain by chain; variables become pointers.</summary>
    private static GraphFrame ListGraph(List<TraceVar> vars, Dictionary<int, (int Next, int Right)> previous)
    {
        var nodes = new Dictionary<int, TraceNode>();
        foreach (var n in vars.SelectMany(v => v.Value.Nodes ?? [])) nodes[n.Ref] = n;
        var targeted = nodes.Values.Select(n => n.Next).Where(nodes.ContainsKey).ToHashSet();
        var heads = nodes.Keys.Where(r => !targeted.Contains(r)).ToList();
        foreach (var v in vars) if (!heads.Contains(v.Value.Ref) && !targeted.Contains(v.Value.Ref)) heads.Add(v.Value.Ref);
        if (heads.Count == 0 && vars.Count > 0) heads.Add(vars[0].Value.Ref); // everything is on a cycle

        const int PerRow = 8;
        var gnodes = new List<GNode>();
        var placed = new HashSet<int>();
        int row = 0;
        foreach (var head in heads)
        {
            int col = 0;
            for (int r = head; r != 0 && nodes.TryGetValue(r, out var n) && placed.Add(r); r = n.Next)
            {
                double x = 7 + (col % PerRow) * 12, y = 8 + (row + col / PerRow) * 13;
                gnodes.Add(new GNode("r" + r, n.Value, x, y, NodeShape.Circle, CellState.Default, null));
                col++;
            }
            row += Math.Max(1, (col + PerRow - 1) / PerRow);
        }
        var edges = nodes.Values.Where(n => n.Next != 0 && nodes.ContainsKey(n.Next))
            .Select(n => new GEdge("e" + n.Ref, "r" + n.Ref, "r" + n.Next, null,
                previous.TryGetValue(n.Ref, out var p) && p.Next != n.Next ? CellState.Active : CellState.Default, true))
            .ToList();
        var pointers = Pointers(vars);
        return new GraphFrame(Math.Max(22, 8 + row * 13 + 4), gnodes, edges, pointers);
    }

    /// <summary>One graph per distinct tree (e.g. p and q); variables pointing into a tree become pointers.</summary>
    private static IEnumerable<(string Title, GraphFrame Graph)> TreeGraphs(List<TraceVar> vars, Dictionary<int, (int Left, int Right)> previous)
    {
        var nodes = new Dictionary<int, TraceNode>();
        foreach (var n in vars.SelectMany(v => v.Value.Nodes ?? [])) nodes[n.Ref] = n;
        var children = nodes.Values.SelectMany(n => new[] { n.Next, n.Right }).Where(r => r != 0).ToHashSet();
        foreach (var root in nodes.Keys.Where(r => !children.Contains(r)))
        {
            var order = new List<(int Ref, int Depth)>();
            var seen = new HashSet<int>();
            void Walk(int r, int depth)
            {
                if (r == 0 || !nodes.TryGetValue(r, out var n) || !seen.Add(r)) return;
                Walk(n.Next, depth + 1);
                order.Add((r, depth));
                Walk(n.Right, depth + 1);
            }
            Walk(root, 0);
            double spacing = Math.Min(11, 92.0 / Math.Max(1, order.Count));
            double start = (100 - spacing * order.Count) / 2;
            var gnodes = order.Select((o, i) => new GNode("r" + o.Ref, nodes[o.Ref].Value, start + (i + 0.5) * spacing, 7 + o.Depth * 12, NodeShape.Circle, CellState.Default, null)).ToList();
            var edges = new List<GEdge>();
            foreach (var (r, _) in order)
            {
                var n = nodes[r];
                bool changed = previous.TryGetValue(r, out var p) && (p.Left != n.Next || p.Right != n.Right);
                if (n.Next != 0 && seen.Contains(n.Next)) edges.Add(new GEdge("l" + r, "r" + r, "r" + n.Next, null, changed ? CellState.Active : CellState.Default, false));
                if (n.Right != 0 && seen.Contains(n.Right)) edges.Add(new GEdge("rt" + r, "r" + r, "r" + n.Right, null, changed ? CellState.Active : CellState.Default, false));
            }
            var inTree = vars.Where(v => seen.Contains(v.Value.Ref)).ToList();
            string title = string.Join(", ", vars.Where(v => v.Value.Ref == root).Select(v => v.Name).DefaultIfEmpty("tree")) + "  ·  TreeNode";
            int maxDepth = order.Count == 0 ? 0 : order.Max(o => o.Depth);
            yield return (title, new GraphFrame(Math.Max(22, 7 + maxDepth * 12 + 10), gnodes, edges, Pointers(inTree)));
        }
    }

    private static List<GPointer> Pointers(List<TraceVar> vars)
    {
        var pointers = new List<GPointer>();
        foreach (var group in vars.GroupBy(v => v.Value.Ref))
        {
            int k = 0;
            foreach (var v in group) pointers.Add(new GPointer(v.Name, "r" + group.Key, k++));
        }
        return pointers;
    }

    private static MapFrame ToMap(TraceValue value, TraceValue? before, List<TraceAccess> accesses)
    {
        var probe = accesses.LastOrDefault(a => a.Kind is "probe" or "read");
        var oldEntries = (before?.Entries ?? []).ToDictionary(x => x.Key, x => x.Value);
        var oldItems = (before?.Items ?? []).ToHashSet();
        IEnumerable<(string Key, string Value)> entries = value.Kind == "set"
            ? (value.Items ?? []).Select(i => (i, ""))
            : (value.Entries ?? []).Select(x => (x.Key, x.Value));
        var views = entries.Select(x =>
        {
            bool isNew = before is not null && (value.Kind == "set" ? !oldItems.Contains(x.Key) : !oldEntries.TryGetValue(x.Key, out var old) || old != x.Value);
            var state = probe?.Key == x.Key ? (probe.Hit == true ? CellState.Found : CellState.Compare) : isNew ? CellState.New : CellState.Default;
            return new MapEntryView(x.Key, x.Value, state);
        }).ToList();
        return new MapFrame(value.Type ?? value.Kind, views, probe?.Key, probe?.Hit);
    }
}
