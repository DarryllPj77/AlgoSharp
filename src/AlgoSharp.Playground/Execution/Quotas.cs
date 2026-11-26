namespace AlgoSharp.Playground.Execution;

/// <summary>Run limits per account and overall.</summary>
public sealed record QuotaLimits(int PerCallerPer10Minutes, int PerCallerPerDay, int PerCallerConcurrent, int GlobalPerMinute);

/// <summary>
/// Per-account and global run limits, kept in memory (one executor instance). A lease must be disposed
/// when the run ends, success or not, to free the account's concurrency slot.
/// </summary>
public sealed class Quotas(QuotaLimits options, TimeProvider clock)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, List<DateTimeOffset>> history = [];
    private readonly Dictionary<string, int> inFlight = [];
    private readonly Queue<DateTimeOffset> global = new();

    public sealed class Lease(Quotas owner, string caller) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 1) return;
            lock (owner.gate)
            {
                if (owner.inFlight.TryGetValue(caller, out int n) && n > 1) owner.inFlight[caller] = n - 1;
                else owner.inFlight.Remove(caller);
            }
        }
    }

    /// <summary>Returns a lease, or null with a user-safe reason.</summary>
    public Lease? TryAcquire(string caller, out string? reason)
    {
        var now = clock.GetUtcNow();
        lock (gate)
        {
            while (global.Count > 0 && now - global.Peek() > TimeSpan.FromMinutes(1)) global.Dequeue();
            if (global.Count >= options.GlobalPerMinute)
            {
                reason = "The code runner is very busy right now. Please try again in a minute.";
                return null;
            }

            if (!history.TryGetValue(caller, out var runs)) history[caller] = runs = [];
            runs.RemoveAll(t => now - t > TimeSpan.FromDays(1));
            if (inFlight.GetValueOrDefault(caller) >= options.PerCallerConcurrent)
            {
                reason = "Your previous run is still in progress. Wait for it to finish, then try again.";
                return null;
            }
            if (runs.Count(t => now - t <= TimeSpan.FromMinutes(10)) >= options.PerCallerPer10Minutes)
            {
                reason = $"You've reached the limit of {options.PerCallerPer10Minutes} runs per 10 minutes. Please wait a few minutes.";
                return null;
            }
            if (runs.Count >= options.PerCallerPerDay)
            {
                reason = $"You've reached today's limit of {options.PerCallerPerDay} runs. It resets within 24 hours.";
                return null;
            }

            runs.Add(now);
            global.Enqueue(now);
            inFlight[caller] = inFlight.GetValueOrDefault(caller) + 1;
            if (history.Count > 50_000) Prune(now);
            reason = null;
            return new Lease(this, caller);
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var key in history.Where(h => h.Value.All(t => now - t > TimeSpan.FromDays(1))).Select(h => h.Key).ToList())
            history.Remove(key);
    }
}

/// <summary>Remembers request nonces for the freshness window, so each signed request works only once.</summary>
public sealed class NonceCache(TimeProvider clock)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, DateTimeOffset> seen = [];

    public bool TryUse(string nonce)
    {
        var now = clock.GetUtcNow();
        lock (gate)
        {
            if (seen.Count > 10_000)
                foreach (var old in seen.Where(s => now - s.Value > TimeSpan.FromMinutes(3)).Select(s => s.Key).ToList()) seen.Remove(old);
            return seen.TryAdd(nonce, now);
        }
    }
}
