using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AlgoSharp.Playground.Compilation;

public enum CompileKind { Check, Run }

public enum GateStatus { Ok, Superseded, Busy, Cooldown, TimedOut, Cancelled }

public sealed record GateResult<T>(GateStatus Status, T? Value, string? Message, TimeSpan RetryAfter = default)
{
    public bool Ok => Status == GateStatus.Ok;
}

/// <summary>Limits for Roslyn work on this server. Defaults fit a small instance (Render Free: 0.1 CPU, 512 MB).</summary>
public sealed class CompileLimits
{
    /// <summary>Compilations running at once across all users. Others wait in a short queue.</summary>
    public int MaxConcurrent { get; set; } = 2;

    /// <summary>Compilations allowed to wait for a slot; beyond this, new requests are turned away at once.</summary>
    public int MaxQueued { get; set; } = 30;

    /// <summary>How long a live check / a Run may wait for a slot before giving up.</summary>
    public double CheckQueueSeconds { get; set; } = 3;
    public double RunQueueSeconds { get; set; } = 10;

    /// <summary>A single compilation is cancelled after this long.</summary>
    public double TimeoutSeconds { get; set; } = 20;

    /// <summary>Minimum time between two Runs of the same user.</summary>
    public double RunCooldownSeconds { get; set; } = 2;
}

/// <summary>
/// The one door every Roslyn compilation goes through. It keeps the server responsive under load:
/// <list type="bullet">
/// <item>a global semaphore caps concurrent compilations; a short, bounded queue absorbs bursts and the rest are
/// told to retry (never an unbounded pile-up of CPU-bound work);</item>
/// <item>live checks are coalesced per user: while one of theirs waits or runs, an older one is dropped in favour
/// of the newest code, so fast typing costs one compilation, not one per keystroke;</item>
/// <item>each user has at most one Run compiling, and Runs have a cooldown;</item>
/// <item>every compilation has a timeout and stops when the user's session goes away.</item>
/// </list>
/// Counters are exported through System.Diagnostics.Metrics ("AlgoSharp") and <see cref="Snapshot"/>.
/// Nothing about the code itself (or the user) is recorded.
/// </summary>
public sealed class CompileGate : IDisposable
{
    public static readonly Meter Meter = new("AlgoSharp");
    private static readonly Histogram<double> DurationMs = Meter.CreateHistogram<double>("algosharp.compile.duration", "ms");
    private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>("algosharp.compile.outcomes");

    private readonly CompileLimits limits;
    private readonly TimeProvider time;
    private readonly SemaphoreSlim slots;
    private readonly ConcurrentDictionary<string, UserState> users = new();
    private int active, queued;
    private long ok, superseded, busy, cooldown, timedOut, cancelled, totalMs, maxMs;

    public CompileGate(CompileLimits limits, TimeProvider time)
    {
        this.limits = limits;
        this.time = time;
        slots = new SemaphoreSlim(Math.Max(1, limits.MaxConcurrent), Math.Max(1, limits.MaxConcurrent));
        Meter.CreateObservableGauge("algosharp.compile.active", () => Volatile.Read(ref active));
        Meter.CreateObservableGauge("algosharp.compile.queued", () => Volatile.Read(ref queued));
    }

    public CompileLimits Limits => limits;

    private sealed class UserState
    {
        public long CheckSeq;
        public int RunInFlight;
        public long LastRunTicks;
        public long LastSeenTicks;
    }

    public async Task<GateResult<T>> RunAsync<T>(string user, CompileKind kind, Func<CancellationToken, T> work, CancellationToken ct = default)
    {
        var state = users.GetOrAdd(user, _ => new UserState());
        long now = time.GetTimestamp();
        Volatile.Write(ref state.LastSeenTicks, now);
        Prune();

        long seq = 0;
        if (kind == CompileKind.Check)
        {
            seq = Interlocked.Increment(ref state.CheckSeq);
        }
        else
        {
            long last = Volatile.Read(ref state.LastRunTicks);
            var since = last == 0 ? TimeSpan.MaxValue : time.GetElapsedTime(last, now);
            var cool = TimeSpan.FromSeconds(limits.RunCooldownSeconds);
            if (since < cool) return Reject<T>(GateStatus.Cooldown, ref cooldown, cool - since);
            if (Interlocked.CompareExchange(ref state.RunInFlight, 1, 0) != 0) return Reject<T>(GateStatus.Cooldown, ref cooldown, TimeSpan.FromSeconds(1));
            Volatile.Write(ref state.LastRunTicks, now);
        }

        try
        {
            if (Interlocked.Increment(ref queued) > limits.MaxQueued + limits.MaxConcurrent)
            {
                Interlocked.Decrement(ref queued);
                return Reject<T>(GateStatus.Busy, ref busy, TimeSpan.FromSeconds(5));
            }
            bool entered;
            try
            {
                var wait = TimeSpan.FromSeconds(kind == CompileKind.Check ? limits.CheckQueueSeconds : limits.RunQueueSeconds);
                entered = await slots.WaitAsync(wait, ct);
            }
            catch (OperationCanceledException)
            {
                return Reject<T>(GateStatus.Cancelled, ref cancelled, default);
            }
            finally
            {
                Interlocked.Decrement(ref queued);
            }
            if (!entered) return Reject<T>(GateStatus.Busy, ref busy, TimeSpan.FromSeconds(5));

            try
            {
                // A newer check from the same user arrived while this one waited: skip the stale code.
                if (kind == CompileKind.Check && Volatile.Read(ref state.CheckSeq) != seq)
                    return Reject<T>(GateStatus.Superseded, ref superseded, default);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(limits.TimeoutSeconds));
                Interlocked.Increment(ref active);
                long started = Stopwatch.GetTimestamp();
                try
                {
                    // CPU-bound Roslyn work runs on the thread pool, not on the caller's (circuit) thread.
                    T value = await Task.Run(() => work(timeout.Token), timeout.Token);
                    Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, kind);
                    Interlocked.Increment(ref ok);
                    Outcomes.Add(1, new KeyValuePair<string, object?>("outcome", "ok"));
                    return new(GateStatus.Ok, value, null);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return Reject<T>(GateStatus.Cancelled, ref cancelled, default);
                }
                catch (OperationCanceledException)
                {
                    return Reject<T>(GateStatus.TimedOut, ref timedOut, TimeSpan.FromSeconds(5));
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            }
            finally
            {
                slots.Release();
            }
        }
        finally
        {
            if (kind == CompileKind.Run) Volatile.Write(ref state.RunInFlight, 0);
        }
    }

    private GateResult<T> Reject<T>(GateStatus status, ref long counter, TimeSpan retryAfter)
    {
        Interlocked.Increment(ref counter);
        Outcomes.Add(1, new KeyValuePair<string, object?>("outcome", status.ToString().ToLowerInvariant()));
        int seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        string? message = status switch
        {
            GateStatus.Busy => $"AlgoSharp is busy checking other people's code right now. Your code is safe and saved; try again in about {seconds} seconds.",
            GateStatus.Cooldown => $"You're running code very quickly. Wait {seconds} second{(seconds == 1 ? "" : "s")} and try again; your code is saved.",
            GateStatus.TimedOut => "Compiling your code took too long and was stopped. Your code is saved; try again, or simplify very large code.",
            _ => null,
        };
        return new(status, default, message, retryAfter);
    }

    private void Record(double ms, CompileKind kind)
    {
        DurationMs.Record(ms, new KeyValuePair<string, object?>("kind", kind.ToString().ToLowerInvariant()));
        Interlocked.Add(ref totalMs, (long)ms);
        long m;
        while ((long)ms > (m = Volatile.Read(ref maxMs)) && Interlocked.CompareExchange(ref maxMs, (long)ms, m) != m) { }
    }

    /// <summary>Forget users idle for 15 minutes, so the table can't grow without bound.</summary>
    private void Prune()
    {
        if (users.Count < 512) return;
        long now = time.GetTimestamp();
        foreach (var (key, s) in users)
        {
            if (time.GetElapsedTime(Volatile.Read(ref s.LastSeenTicks), now) > TimeSpan.FromMinutes(15)
                && Volatile.Read(ref s.RunInFlight) == 0)
                users.TryRemove(key, out _);
        }
    }

    public sealed record Stats(int Active, int Queued, long Ok, long Superseded, long Busy, long Cooldown, long TimedOut, long Cancelled, double AvgMs, long MaxMs, int Users);

    public Stats Snapshot()
    {
        long done = Volatile.Read(ref ok);
        return new(Volatile.Read(ref active), Volatile.Read(ref queued), done, Volatile.Read(ref superseded), Volatile.Read(ref busy),
            Volatile.Read(ref cooldown), Volatile.Read(ref timedOut), Volatile.Read(ref cancelled),
            done == 0 ? 0 : Math.Round((double)Volatile.Read(ref totalMs) / done, 1), Volatile.Read(ref maxMs), users.Count);
    }

    public void Dispose() => slots.Dispose();
}
