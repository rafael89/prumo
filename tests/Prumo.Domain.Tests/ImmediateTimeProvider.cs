namespace Prumo.Domain.Tests;

// Run retry waits immediately while recording the requested intervals.
internal sealed class ImmediateTimeProvider : TimeProvider
{
    public List<TimeSpan> Delays { get; } = new();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Delays.Add(dueTime);
        return new Timer(callback, state, TimeSpan.Zero, period);
    }
}
