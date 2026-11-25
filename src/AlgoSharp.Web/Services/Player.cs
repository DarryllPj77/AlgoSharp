using AlgoSharp.Core.Engine;

namespace AlgoSharp.Web.Services;

/// <summary>
/// Deterministic step playback. Every step is a full snapshot, so moving to any index is
/// just "render snapshot i". A single cancellation source guarantees only one play loop
/// runs at a time, no matter how quickly the user clicks.
/// </summary>
public sealed class Player : IDisposable
{
    private CancellationTokenSource? loop;

    public static readonly double[] Speeds = [0.25, 0.5, 1, 1.5, 2, 3, 4];

    public IReadOnlyList<Step> Steps { get; private set; } = [];
    public int Index { get; private set; }
    public bool Playing => loop is not null;
    public double Speed { get; set; } = 1;
    public bool Scrubbing { get; private set; }

    public Step? Current => Steps.Count == 0 ? null : Steps[Index];
    public Step? Previous => Index > 0 && Steps.Count > 0 ? Steps[Index - 1] : null;
    public bool AtStart => Index == 0;
    public bool AtEnd => Steps.Count == 0 || Index >= Steps.Count - 1;

    /// <summary>Milliseconds between steps while playing.</summary>
    public int Interval => (int)(1000 / Speed);

    /// <summary>Animation duration for transitions; shorter than the interval so motion settles.</summary>
    public int AnimationMs => Scrubbing ? 140 : Math.Clamp((int)(Interval * 0.7), 90, 900);

    /// <summary>Raised on every change; subscribers re-render.</summary>
    public event Action? Changed;

    public void Load(IReadOnlyList<Step> steps, bool autoplay)
    {
        Stop();
        Steps = steps;
        Index = 0;
        Scrubbing = false;
        Changed?.Invoke();
        if (autoplay && steps.Count > 1) Play();
    }

    public void Play()
    {
        if (Steps.Count < 2) return;
        Stop();
        if (AtEnd) Index = 0;
        Scrubbing = false;
        var cts = new CancellationTokenSource();
        loop = cts;
        Changed?.Invoke();
        _ = RunAsync(cts);
    }

    public void Pause()
    {
        Stop();
        Changed?.Invoke();
    }

    public void Toggle()
    {
        if (Playing) Pause(); else Play();
    }

    public void Forward() => Go(Index + 1, scrub: false);

    public void Back() => Go(Index - 1, scrub: false);

    public void First() => Go(0, scrub: false);

    public void Last() => Go(Steps.Count - 1, scrub: false);

    public void Seek(int index) => Go(index, scrub: true);

    public void SetSpeed(double speed)
    {
        Speed = speed;
        Changed?.Invoke();
    }

    private void Go(int index, bool scrub)
    {
        Stop();
        if (Steps.Count == 0) return;
        Index = Math.Clamp(index, 0, Steps.Count - 1);
        Scrubbing = scrub;
        Changed?.Invoke();
    }

    private async Task RunAsync(CancellationTokenSource cts)
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(Interval, cts.Token);
                if (cts.IsCancellationRequested || loop != cts) return;
                if (AtEnd)
                {
                    Stop();
                    Changed?.Invoke();
                    return;
                }
                Index++;
                Changed?.Invoke();
            }
        }
        catch (TaskCanceledException)
        {
        }
    }

    private void Stop()
    {
        if (loop is null) return;
        loop.Cancel();
        loop.Dispose();
        loop = null;
    }

    public void Dispose()
    {
        Stop();
        Changed = null;
    }
}
