namespace AlgoSharp.Core.Engine;

/// <summary>
/// Records steps for dynamic-programming tables. Cell values are read from the real DP array
/// through <c>value</c>; only cells passed to <see cref="Fill"/> are shown, so the table visibly
/// fills in the order the algorithm computes it.
/// </summary>
public sealed class TableTracer
{
    private readonly IReadOnlyList<string> rowHeaders;
    private readonly IReadOnlyList<string> colHeaders;
    private Func<int, int, string> value;
    private readonly string corner;
    private readonly HashSet<(int, int)> filled = [];
    private readonly Dictionary<(int, int), CellState> marks = [];

    public TableTracer(IReadOnlyList<string> rowHeaders, IReadOnlyList<string> colHeaders, string corner = "")
    {
        this.rowHeaders = rowHeaders;
        this.colHeaders = colHeaders;
        this.corner = corner;
        value = (_, _) => "";
    }

    /// <summary>Tells the tracer how to read a cell of the algorithm's own DP array.</summary>
    public void Read(Func<int, int, string> reader) => value = reader;

    public List<Step> Steps { get; } = [];

    public int Rows => rowHeaders.Count;
    public int Cols => colHeaders.Count;

    public void Fill(int r, int c) => filled.Add((r, c));

    public void FillRow(int r)
    {
        for (int c = 0; c < Cols; c++) Fill(r, c);
    }

    public void FillCol(int c)
    {
        for (int r = 0; r < Rows; r++) Fill(r, c);
    }

    public void FillAll()
    {
        for (int r = 0; r < Rows; r++) FillRow(r);
    }

    public void Mark(CellState state, int r, int c) => marks[(r, c)] = state;

    public TableStepBuilder At(string label) => new(this, label);

    public Step Snapshot(string message) => At("").Say(message, record: false);

    internal Step Emit(TableStepBuilder b, bool record)
    {
        var rows = new List<IReadOnlyList<TableCell>>(Rows);
        for (int r = 0; r < Rows; r++)
        {
            var cells = new List<TableCell>(Cols);
            for (int c = 0; c < Cols; c++)
            {
                var state = b.HighlightFor(r, c) ?? marks.GetValueOrDefault((r, c), CellState.Default);
                cells.Add(new TableCell(filled.Contains((r, c)) ? value(r, c) : "", state));
            }
            rows.Add(cells);
        }
        var step = new Step(b.Label, b.Message, b.Vars, IsError: b.IsError, Table: new TableFrame(rowHeaders, colHeaders, rows, corner));
        if (record) Steps.Add(step);
        return step;
    }
}

public sealed class TableStepBuilder
{
    private readonly TableTracer tracer;
    private readonly List<(CellState State, int R, int C)> highlights = [];

    internal TableStepBuilder(TableTracer tracer, string label)
    {
        this.tracer = tracer;
        Label = label;
    }

    internal string Label { get; }
    internal string Message { get; private set; } = "";
    internal bool IsError { get; private set; }
    internal List<VarView> Vars { get; } = [];

    public TableStepBuilder Hl(CellState state, int r, int c)
    {
        if (r >= 0 && c >= 0) highlights.Add((state, r, c));
        return this;
    }

    public TableStepBuilder V(string name, object? value)
    {
        Vars.Add(new VarView(name, ArrayStepBuilder.Format(value)));
        return this;
    }

    public Step Say(string message, bool record = true)
    {
        Message = message;
        return tracer.Emit(this, record);
    }

    internal CellState? HighlightFor(int r, int c)
    {
        for (int i = highlights.Count - 1; i >= 0; i--)
        {
            if (highlights[i].R == r && highlights[i].C == c) return highlights[i].State;
        }
        return null;
    }
}
