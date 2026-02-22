using System.Diagnostics;

public sealed class ActivityTracker
{
    private readonly IRaftClock _clock;
    private long _lastActivityTicks;

    public ActivityTracker(IRaftClock clock)
    {
        _clock = clock;
        _lastActivityTicks = clock.Now;
    }

    public long Now => _clock.Now;

    public long LastActivityTicks
    {
        get => Interlocked.Read(ref _lastActivityTicks);
        set => Interlocked.Exchange(ref _lastActivityTicks, value);
    }
}
