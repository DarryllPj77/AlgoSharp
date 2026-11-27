using AlgoSharp.Core.Engine;

namespace AlgoSharp.Web.Services;

/// <summary>
/// A short sound for every step the player moves through (playing, or stepping with next/back), chosen from
/// what the step shows: a comparison blips at the pitch of the values being compared, a swap plays both,
/// an element reaching its final place or a found value rings, inserts rise, removals fall, errors are low.
/// Pitch follows the value (small = low, large = high), so a sort audibly climbs as it orders the array.
/// Jumps (scrubbing, first/last, loading a new run) stay silent.
/// </summary>
public sealed class StepSounds(SoundCues sound)
{
    private IReadOnlyList<Step>? steps;
    private int last = -1;

    /// <param name="skip">True when this step already gets another cue (e.g. the run's finishing chime).</param>
    public void OnChanged(Player player, bool skip = false)
    {
        bool sameRun = ReferenceEquals(steps, player.Steps);
        int previous = last;
        steps = player.Steps;
        last = player.Index;
        if (!sameRun || skip || player.Scrubbing || Math.Abs(player.Index - previous) != 1 || player.Current is not { } step) return;
        var (kind, pitches) = Describe(step, player.Previous);
        _ = sound.StepAsync(kind, pitches);
    }

    public static (string Kind, double[] Pitches) Describe(Step step, Step? previous)
    {
        if (step.IsError) return ("error", []);
        var now = Items(step);
        if (now.Any(i => i.State == CellState.Error)) return ("error", []);

        double[] Pitches(IEnumerable<Item> items) => [.. items.Take(2).Select(i => i.Pitch)];
        Item[] With(params CellState[] states) => [.. now.Where(i => states.Contains(i.State))];

        if (With(CellState.Swap) is { Length: > 0 } swapped) return ("swap", Pitches(swapped));

        // Something newly reached its final place or was found: compare against the previous step.
        var settled = With(CellState.Sorted, CellState.Found);
        int before = previous is null ? 0 : Items(previous).Count(i => i.State is CellState.Sorted or CellState.Found);
        if (settled.Length > before) return ("place", Pitches(settled.Reverse()));
        if (step.Map is { ProbeHit: true }) return ("place", [784]);

        if (With(CellState.New) is { Length: > 0 } added) return ("add", Pitches(added));
        if (With(CellState.Removed) is { Length: > 0 } removed) return ("remove", Pitches(removed));
        if (With(CellState.Compare, CellState.Pivot, CellState.Key, CellState.Min, CellState.Active) is { Length: > 0 } focus)
            return ("compare", Pitches(focus));
        if (step.Map is { ProbeHit: false }) return ("compare", [330]);
        return ("tick", []);
    }

    private readonly record struct Item(CellState State, double Pitch);

    /// <summary>Every highlighted element of the step, with a pitch from its value (220–880 Hz).</summary>
    private static List<Item> Items(Step step)
    {
        var items = new List<Item>();
        void Add(IEnumerable<(CellState State, double? Value)> source)
        {
            var list = source.ToList();
            double max = Math.Max(1, list.Where(x => x.Value is not null).Select(x => Math.Abs(x.Value!.Value)).DefaultIfEmpty(1).Max());
            foreach (var (state, value) in list)
            {
                if (state == CellState.Default) continue;
                double ratio = value is { } v ? Math.Clamp(Math.Abs(v) / max, 0, 1) : 0.5;
                items.Add(new Item(state, 220 * Math.Pow(2, 2 * ratio)));
            }
        }
        if (step.Array is { } a) Add(a.Cells.Select(c => (c.State, (double?)c.Value)));
        if (step.List is { } l) Add(l.Nodes.Select(n => (n.State, (double?)n.Value)));
        if (step.Graph is { } g) Add(g.Nodes.Select(n => (n.State, double.TryParse(n.Label, out var v) ? v : (double?)null)));
        if (step.Callout is { } c) Add([(c.Tone, null)]);
        return items;
    }
}
