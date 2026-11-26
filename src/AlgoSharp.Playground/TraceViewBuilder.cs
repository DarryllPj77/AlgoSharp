using AlgoSharp.Core.Engine;
using AlgoSharp.Tracing;

namespace AlgoSharp.Playground;

/// <summary>A visual for one container variable at one step.</summary>
public sealed record ContainerView(string Title, ArrayFrame? Array, MapFrame? Map);

/// <summary>One replayable frame: the step (line, message, variables) plus its container visuals.</summary>
public sealed record TraceFrame(Step Step, int Line, IReadOnlyList<ContainerView> Containers, TraceEventKind Kind);

/// <summary>
/// Turns a recorded trace into frames for the existing visual components. Everything shown
/// comes from the trace: values, which elements were read, which keys were looked up, and
/// which variables were used as indices. Nothing is inferred from the algorithm's identity.
/// </summary>
public static class TraceViewBuilder
{
    private static readonly HashSet<string> SequenceKinds = ["array", "list", "stack", "queue"];
    private static readonly HashSet<string> MapKinds = ["dict", "set"];

    public static List<TraceFrame> Build(CaseResult result, IReadOnlyList<string> sourceLines)
    {
        var frames = new List<TraceFrame>();
        var previousValues = new Dictionary<int, TraceValue>();
        var indexNames = new Dictionary<int, List<string>>();
        var lastScalars = new Dictionary<(int Depth, string Name), string>();
        long conditions = 0, reads = 0, lookups = 0;

        foreach (var e in result.Events)
        {
            conditions += e.Kind == TraceEventKind.Condition ? 1 : 0;
            reads += e.Accesses.Count(a => a.Kind == "read");
            lookups += e.Accesses.Count(a => a.Kind == "probe");
            foreach (var a in e.Accesses.Where(a => a.IndexName is not null))
            {
                var names = indexNames.TryGetValue(a.Ref, out var l) ? l : indexNames[a.Ref] = [];
                if (!names.Contains(a.IndexName!)) names.Add(a.IndexName!);
            }

            var scalars = e.Vars.Where(v => !SequenceKinds.Contains(v.Value.Kind) && !MapKinds.Contains(v.Value.Kind)).ToList();
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

            var vars = scalars.Select(v => new VarView(v.Name, v.Value.Text ?? v.Value.Kind)).ToList();
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

        var layout = value.Kind == "stack" ? ArrayLayout.Stack : ArrayLayout.Cells;
        return new ArrayFrame(layout, Math.Max(1, items.Count), cells, pointers, -1, -1, false, 1);
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
