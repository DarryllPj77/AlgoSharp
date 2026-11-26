using System.Globalization;

namespace AlgoSharp.Core.Engine;

/// <summary>
/// Records execution steps for array-based algorithms. It mirrors the real array with
/// identity-tracked cells, so a value that moves (swap, shift, merge) keeps its identity
/// and the UI can animate the movement instead of redrawing.
/// </summary>
public sealed class ArrayTracer
{
    private sealed class Cell
    {
        public int Id;
        public int Value;
        public int Pos;
        public int Row;
        public CellState Mark;
    }

    private readonly List<Cell> cells = [];
    private readonly List<Cell> leaving = [];
    private readonly HashSet<int> fresh = [];
    private int nextId;
    private int rangeLo = -1, rangeHi = -1;
    private bool hasAux;
    private int maxValue = 1;
    private readonly Dictionary<string, long> counters = [];
    private Func<IEnumerable<KeyValuePair<int, int>>>? map;
    private string mapTitle = "";
    private static readonly Step Skipped = new("", "", []);

    /// <summary>
    /// When false, steps are not recorded but counters still tick. Used to measure operation
    /// counts on large inputs with the same code that is visualized.
    /// </summary>
    public bool Recording { get; init; } = true;

    /// <summary>Counts one operation (comparison, lookup, ...). Shown live and in comparisons.</summary>
    public void Tick(string counter, int by = 1) => counters[counter] = counters.GetValueOrDefault(counter) + by;

    public long Count(string counter) => counters.GetValueOrDefault(counter);

    public IReadOnlyDictionary<string, long> Counters => counters;

    /// <summary>Declares a counter at 0 so it is displayed from the first step.</summary>
    public void Counter(string counter) => counters.TryAdd(counter, 0);

    /// <summary>Shows a real dictionary next to the array; it is read on every snapshot.</summary>
    public void Watch(IReadOnlyDictionary<int, int> dictionary, string title)
    {
        map = () => dictionary;
        mapTitle = title;
    }

    public ArrayTracer(ArrayLayout layout, int capacity, IEnumerable<int>? values = null)
    {
        Layout = layout;
        Capacity = capacity;
        int pos = 0;
        foreach (int v in values ?? [])
        {
            cells.Add(new Cell { Id = nextId++, Value = v, Pos = pos++ });
        }
        RecalcMax();
    }

    public ArrayLayout Layout { get; }
    public int Capacity { get; }
    public List<Step> Steps { get; private set; } = [];

    /// <summary>Starts a new recording (the array model itself is kept).</summary>
    public void BeginOperation()
    {
        Steps = [];
        rangeLo = rangeHi = -1;
        foreach (var c in cells)
        {
            if (c.Mark is not CellState.Sorted) c.Mark = CellState.Default;
        }
    }

    // ---------- structural changes (mirror the real array) ----------

    public void Swap(int i, int j)
    {
        if (i == j) return;
        var a = MainAt(i);
        var b = MainAt(j);
        if (a is not null) a.Pos = j;
        if (b is not null) b.Pos = i;
    }

    /// <summary>Moves the cell at main position <paramref name="from"/> to an empty slot <paramref name="to"/>.</summary>
    public void Move(int from, int to)
    {
        var c = MainAt(from) ?? throw new InvalidOperationException($"No cell at {from}");
        if (MainAt(to) is { } occupied && occupied != c)
            throw new InvalidOperationException($"Slot {to} is occupied");
        c.Pos = to;
    }

    /// <summary>Like <see cref="Move"/>, but does nothing when the source slot is empty.</summary>
    public void MoveIfPresent(int from, int to)
    {
        if (MainAt(from) is not null) Move(from, to);
    }

    public void Lift(int pos)
    {
        var c = MainAt(pos) ?? throw new InvalidOperationException($"No cell at {pos}");
        c.Row = 1;
        hasAux = true;
    }

    public void LiftRange(int lo, int hi)
    {
        for (int p = lo; p <= hi; p++) Lift(p);
    }

    public void MoveAux(int from, int to)
    {
        var c = AuxAt(from) ?? throw new InvalidOperationException($"No aux cell at {from}");
        c.Pos = to;
    }

    public void Drop(int auxPos, int toPos)
    {
        var c = AuxAt(auxPos) ?? throw new InvalidOperationException($"No aux cell at {auxPos}");
        if (MainAt(toPos) is not null) throw new InvalidOperationException($"Slot {toPos} is occupied");
        c.Row = 0;
        c.Pos = toPos;
    }

    public void Create(int pos, int value)
    {
        if (MainAt(pos) is not null) throw new InvalidOperationException($"Slot {pos} is occupied");
        var c = new Cell { Id = nextId++, Value = value, Pos = pos };
        cells.Add(c);
        fresh.Add(c.Id);
        RecalcMax();
    }

    public void Remove(int pos)
    {
        var c = MainAt(pos) ?? throw new InvalidOperationException($"No cell at {pos}");
        cells.Remove(c);
        leaving.Add(c);
    }

    public void SetValue(int pos, int value)
    {
        var c = MainAt(pos) ?? throw new InvalidOperationException($"No cell at {pos}");
        c.Value = value;
        fresh.Add(c.Id);
        RecalcMax();
    }

    // ---------- persistent marks ----------

    public void Mark(CellState state, params int[] positions)
    {
        foreach (int p in positions)
        {
            if (MainAt(p) is { } c) c.Mark = state;
        }
    }

    public void MarkRange(CellState state, int lo, int hi)
    {
        for (int p = lo; p <= hi; p++) Mark(state, p);
    }

    public void MarkAll(CellState state)
    {
        foreach (var c in cells) c.Mark = state;
    }

    public void ClearMarks()
    {
        foreach (var c in cells) c.Mark = CellState.Default;
    }

    /// <summary>Focuses a sub-range; cells outside it are dimmed.</summary>
    public void Range(int lo, int hi)
    {
        rangeLo = lo;
        rangeHi = hi;
    }

    public void ClearRange() => rangeLo = rangeHi = -1;

    /// <summary>Values currently in the main row, ordered by position (gaps skipped).</summary>
    public int[] MainValues() => cells.Where(c => c.Row == 0).OrderBy(c => c.Pos).Select(c => c.Value).ToArray();

    public int?[] Slots()
    {
        var slots = new int?[Capacity];
        foreach (var c in cells.Where(c => c.Row == 0 && c.Pos >= 0 && c.Pos < Capacity)) slots[c.Pos] = c.Value;
        return slots;
    }

    public ArrayStepBuilder At(string label) => new(this, label);

    /// <summary>Snapshot of the current model without highlights.</summary>
    public Step Snapshot(string message) => At("").Say(message, record: false);

    internal Step Emit(ArrayStepBuilder b, bool record)
    {
        if (!Recording) return Skipped;
        var views = new List<CellView>(cells.Count + leaving.Count);
        foreach (var c in cells)
        {
            CellState state = b.HighlightFor(c.Row, c.Pos)
                ?? (fresh.Contains(c.Id) ? CellState.New : c.Mark);
            if (state == CellState.Default && rangeLo >= 0 && c.Row == 0 && (c.Pos < rangeLo || c.Pos > rangeHi))
                state = CellState.Dim;
            views.Add(new CellView(c.Id, c.Value, c.Pos, c.Row, state));
        }
        foreach (var c in leaving)
        {
            views.Add(new CellView(c.Id, c.Value, c.Pos, c.Row, CellState.Removed));
        }
        views.Sort((x, y) => x.Id.CompareTo(y.Id));

        var pointers = new List<PointerView>();
        foreach (var group in b.Pointers.GroupBy(p => (p.Pos, p.Row)))
        {
            int k = 0;
            foreach (var p in group) pointers.Add(new PointerView(p.Name, p.Pos, p.Row, k++));
        }

        var frame = new ArrayFrame(Layout, Capacity, views, pointers, rangeLo, rangeHi, hasAux, maxValue);
        var step = new Step(b.Label, b.Message, b.Vars, Array: frame, IsError: b.IsError)
        {
            Counters = counters.Select(c => new VarView(c.Key, c.Value.ToString())).ToList(),
            Callout = b.CalloutText is null ? null : new Callout(b.CalloutText, b.CalloutTone),
            Map = map is null ? null : new MapFrame(
                mapTitle,
                map().Select(e => new MapEntryView(e.Key.ToString(), e.Value.ToString(), b.KeyHighlightFor(e.Key) ?? CellState.Default)).ToList(),
                b.Probe?.ToString(),
                b.Probe is { } probe ? map().Any(e => e.Key == probe) : null),
        };
        if (record)
        {
            Steps.Add(step);
            leaving.Clear();
            fresh.Clear();
        }
        return step;
    }

    private Cell? MainAt(int pos) => cells.FirstOrDefault(c => c.Row == 0 && c.Pos == pos);

    private Cell? AuxAt(int pos) => cells.FirstOrDefault(c => c.Row == 1 && c.Pos == pos);

    private void RecalcMax() => maxValue = Math.Max(1, cells.Count == 0 ? 1 : cells.Max(c => Math.Abs(c.Value)));
}

public sealed class ArrayStepBuilder
{
    private readonly ArrayTracer tracer;
    private readonly List<(CellState State, int Row, int Pos)> highlights = [];

    internal ArrayStepBuilder(ArrayTracer tracer, string label)
    {
        this.tracer = tracer;
        Label = label;
    }

    internal string Label { get; }
    internal string Message { get; private set; } = "";
    internal bool IsError { get; private set; }
    internal List<VarView> Vars { get; } = [];
    internal List<(string Name, int Pos, int Row)> Pointers { get; } = [];
    internal string? CalloutText { get; private set; }
    internal CellState CalloutTone { get; private set; }
    internal int? Probe { get; private set; }
    private readonly List<(CellState State, int Key)> keyHighlights = [];

    /// <summary>A short expression shown prominently for this step.</summary>
    public ArrayStepBuilder Callout(string text, CellState tone = CellState.Default)
    {
        CalloutText = text;
        CalloutTone = tone;
        return this;
    }

    /// <summary>Highlights a key in the watched dictionary.</summary>
    public ArrayStepBuilder HlKey(CellState state, int key)
    {
        keyHighlights.Add((state, key));
        return this;
    }

    /// <summary>Shows a dictionary lookup of <paramref name="key"/> (hit or miss is computed).</summary>
    public ArrayStepBuilder Lookup(int key)
    {
        Probe = key;
        return this;
    }

    internal CellState? KeyHighlightFor(int key)
    {
        for (int i = keyHighlights.Count - 1; i >= 0; i--)
        {
            if (keyHighlights[i].Key == key) return keyHighlights[i].State;
        }
        return null;
    }

    /// <summary>Highlights main-row positions for this step only.</summary>
    public ArrayStepBuilder Hl(CellState state, params int[] positions)
    {
        foreach (int p in positions) highlights.Add((state, 0, p));
        return this;
    }

    /// <summary>Highlights auxiliary-row (temporary buffer) positions for this step only.</summary>
    public ArrayStepBuilder HlAux(CellState state, params int[] positions)
    {
        foreach (int p in positions) highlights.Add((state, 1, p));
        return this;
    }

    public ArrayStepBuilder Ptr(string name, int pos)
    {
        if (pos >= 0 && pos < tracer.Capacity) Pointers.Add((name, pos, 0));
        return this;
    }

    public ArrayStepBuilder PtrAux(string name, int pos)
    {
        if (pos >= 0 && pos < tracer.Capacity) Pointers.Add((name, pos, 1));
        return this;
    }

    public ArrayStepBuilder V(string name, object? value)
    {
        Vars.Add(new VarView(name, Format(value)));
        return this;
    }

    public Step Say(string message, bool record = true)
    {
        Message = message;
        return tracer.Emit(this, record);
    }

    public Step Error(string message)
    {
        IsError = true;
        return Say(message);
    }

    internal CellState? HighlightFor(int row, int pos)
    {
        for (int i = highlights.Count - 1; i >= 0; i--)
        {
            if (highlights[i].Row == row && highlights[i].Pos == pos) return highlights[i].State;
        }
        return null;
    }

    internal static string Format(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        int[] arr => "[" + string.Join(", ", arr) + "]",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
