namespace NovaIsland.Services;

internal sealed class FocusSession(TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private long started;
    private double remaining;
    private double duration;
    private string phase = "idle";
    internal (string Phase, int Remaining, int Duration) Snapshot()
    {
        double seconds = phase == "running" ? Math.Max(0, remaining - clock.GetElapsedTime(started).TotalSeconds) : remaining;
        if (phase == "running" && seconds <= 0) { phase = "complete"; remaining = 0; }
        return (phase, (int)Math.Ceiling(seconds), (int)duration);
    }
    internal void Start(int minutes)
    {
        if (minutes is < 1 or > 180) throw new ArgumentOutOfRangeException(nameof(minutes));
        remaining = duration = minutes * 60;
        started = clock.GetTimestamp();
        phase = "running";
    }
    internal void Toggle()
    {
        var state = Snapshot();
        if (state.Phase == "running") { remaining = Math.Max(0, remaining - clock.GetElapsedTime(started).TotalSeconds); phase = "paused"; }
        else if (state.Phase == "paused") { started = clock.GetTimestamp(); phase = "running"; }
    }
    internal void Reset() { remaining = duration = 0; phase = "idle"; }
}
