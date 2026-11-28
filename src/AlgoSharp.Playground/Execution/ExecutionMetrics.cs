using System.Diagnostics.Metrics;
using AlgoSharp.Playground.Compilation;

namespace AlgoSharp.Playground.Execution;

/// <summary>
/// Run outcomes after compilation (ok, timeout, crashed, invalid trace, ...) and the server's time spent turning a
/// returned trace into frames. Counts and durations only: never code, traces or users.
/// </summary>
public static class ExecutionMetrics
{
    private static readonly Counter<long> Outcomes = CompileGate.Meter.CreateCounter<long>("algosharp.execution.outcomes");
    private static readonly Histogram<double> InterpretMs = CompileGate.Meter.CreateHistogram<double>("algosharp.trace.interpret.duration", "ms");

    private static readonly string[] Kinds = ["ok", "timeout", "crashed", "startup", "unsupported", "invalid-trace", "render-failed", "failed"];
    private static readonly long[] counts = new long[Kinds.Length];
    private static long interpretTotalMs, interpretCount, interpretMaxMs;

    public static void Record(string kind)
    {
        int i = Array.IndexOf(Kinds, kind);
        if (i < 0) i = Kinds.Length - 1;
        Interlocked.Increment(ref counts[i]);
        Outcomes.Add(1, new KeyValuePair<string, object?>("outcome", Kinds[i]));
    }

    public static void RecordInterpret(double ms)
    {
        InterpretMs.Record(ms);
        Interlocked.Add(ref interpretTotalMs, (long)ms);
        Interlocked.Increment(ref interpretCount);
        long m;
        while ((long)ms > (m = Volatile.Read(ref interpretMaxMs)) && Interlocked.CompareExchange(ref interpretMaxMs, (long)ms, m) != m) { }
    }

    /// <summary>"ok=12 timeout=1 ..." (non-zero only) plus interpret timing, for the resource log.</summary>
    public static (string Outcomes, double InterpretAvgMs, long InterpretMaxMs) Snapshot()
    {
        var parts = Kinds.Select((k, i) => (k, n: Volatile.Read(ref counts[i]))).Where(x => x.n > 0).Select(x => $"{x.k}:{x.n}");
        long n = Volatile.Read(ref interpretCount);
        return (string.Join(",", parts) is { Length: > 0 } s ? s : "none",
                n == 0 ? 0 : Math.Round((double)Volatile.Read(ref interpretTotalMs) / n, 1),
                Volatile.Read(ref interpretMaxMs));
    }
}
