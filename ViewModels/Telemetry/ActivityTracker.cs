using System.Threading;

namespace KafkaLens.ViewModels.Telemetry;

/// <summary>
/// Tracks whether the user is actively using the app (window focused and recent input).
/// Fed by the view layer; read by the telemetry heartbeat. Thread-safe.
/// </summary>
public sealed class ActivityTracker
{
    private readonly Func<long> tickCount;
    private long lastInputMs;
    private int focused;

    public ActivityTracker(Func<long>? tickCount = null)
    {
        this.tickCount = tickCount ?? (() => Environment.TickCount64);
        lastInputMs = this.tickCount();
    }

    public void RecordInput() => Interlocked.Exchange(ref lastInputMs, tickCount());

    public void SetFocused(bool isFocused)
    {
        Interlocked.Exchange(ref focused, isFocused ? 1 : 0);
        if (isFocused) RecordInput();
    }

    public bool IsActive(TimeSpan idleThreshold) =>
        Volatile.Read(ref focused) == 1 &&
        tickCount() - Interlocked.Read(ref lastInputMs) <= (long)idleThreshold.TotalMilliseconds;
}
