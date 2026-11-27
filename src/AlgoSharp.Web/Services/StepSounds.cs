using AlgoSharp.Core.Engine;

namespace AlgoSharp.Web.Services;

/// <summary>
/// One short sound per real step transition of a <see cref="Player"/> (playing, or Next/Previous), chosen from
/// what changed on screen. Shared by the algorithm visualizers and Solve It playback.
/// <list type="bullet">
/// <item>comparison or condition → soft tick (pitched by the values compared)</item>
/// <item>swap, insert, removal (movement) → short pop</item>
/// <item>element access or variable update → subtle click</item>
/// <item>a value reaching its final place / found → small chime</item>
/// <item>error → gentle warning; reaching the last step by playing or stepping → completion cue</item>
/// </list>
/// Silent on jumps: loading a run, seeking, First/Last, and restarting from the end.
/// </summary>
public sealed class StepSounds(SoundCues sound)
{
    public const string Tick = "tick", Pop = "pop", Click = "click", Place = "place", Warn = "warn";

    private IReadOnlyList<Step>? steps;
    private int last = -1;

    /// <param name="endCue">Cue for arriving at the last step (e.g. success or fail); null = a step sound as usual.</param>
    public void OnChanged(Player player, Func<Step, string?>? endCue = null)
    {
        bool sameRun = ReferenceEquals(steps, player.Steps);
        int from = last;
        steps = player.Steps;
        last = player.Index;
        if (!sameRun || player.Scrubbing || Math.Abs(player.Index - from) != 1 || player.Current is not { } step) return;

        bool arrived = player.Index > from && player.AtEnd;
        if (arrived && endCue?.Invoke(step) is { } cue)
        {
            _ = sound.PlayAsync(cue);
            return;
        }
        var (kind, pitch) = Describe(step, player.Steps[from]);
        _ = sound.StepAsync(kind, pitch);
    }

    /// <summary>The sound for moving from <paramref name="from"/> to <paramref name="step"/>, with a pitch from the values involved.</summary>
    public static (string Kind, double Pitch) Describe(Step step, Step? from)
    {
        var now = Items(step);
        if (step.IsError || step.Callout?.Tone == CellState.Error || now.Any(i => i.State == CellState.Error)) return (Warn, 0);

        double PitchOf(params CellState[] states) => now.FirstOrDefault(i => states.Contains(i.State)) is { State: not CellState.Default } i ? i.Pitch : 0;

        if (now.Any(i => i.State is CellState.Swap or CellState.New or CellState.Removed))
            return (Pop, PitchOf(CellState.Swap, CellState.New, CellState.Removed));

        int settled = now.Count(i => i.State is CellState.Sorted or CellState.Found);
        int settledBefore = from is null ? 0 : Items(from).Count(i => i.State is CellState.Sorted or CellState.Found);
        if (settled > settledBefore || step.Map is { ProbeHit: true }) return (Place, PitchOf(CellState.Found, CellState.Sorted));

        bool condition = step.Callout is { Tone: CellState.Compare or CellState.Found };
        if (condition || now.Any(i => i.State is CellState.Compare or CellState.Pivot or CellState.Min) || step.Map is { ProbeHit: false })
            return (Tick, PitchOf(CellState.Compare, CellState.Pivot, CellState.Min));

        // Accesses, pointer moves, variable updates and narration: the quietest sound.
        return (Click, PitchOf(CellState.Key, CellState.Active, CellState.Visited));
    }

    private readonly record struct Item(CellState State, double Pitch);

    /// <summary>Highlighted elements of a step with a pitch from their value (220–880 Hz; 0 = no value).</summary>
    private static List<Item> Items(Step step)
    {
        var items = new List<Item>();
        void Add(IEnumerable<(CellState State, double? Value)> source)
        {
            var all = source.ToList();
            // Scale against every value shown, so a value keeps the same pitch from step to step.
            double max = Math.Max(1, all.Where(x => x.Value is not null).Select(x => Math.Abs(x.Value!.Value)).DefaultIfEmpty(1).Max());
            foreach (var (state, value) in all.Where(x => x.State != CellState.Default))
                items.Add(new Item(state, value is { } v ? 220 * Math.Pow(2, 2 * Math.Clamp(Math.Abs(v) / max, 0, 1)) : 0));
        }
        static double? Num(string? s) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

        if (step.Array is { } a) Add(a.Cells.Select(c => (c.State, (double?)c.Value)));
        if (step.List is { } l) Add(l.Nodes.Select(n => (n.State, (double?)n.Value)));
        if (step.Graph is { } g) Add(g.Nodes.Select(n => (n.State, Num(n.Label))));
        if (step.Map is { } m) Add(m.Entries.Select(e => (e.State, Num(e.Value))));
        return items;
    }
}
