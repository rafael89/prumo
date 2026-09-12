using Prumo.Worker;

namespace Prumo.Domain.Tests;

public sealed class DocumentDiscoveryIntervalTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Normalizes_to_utc_and_uses_inclusive_bounds()
    {
        var interval = DocumentDiscoveryInterval.Create(
            new DateTimeOffset(2026, 9, 1, 21, 0, 0, TimeSpan.FromHours(-3)),
            new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.FromHours(-3)),
            Now);

        Assert.Equal(new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero), interval.From);
        Assert.Equal(new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero), interval.To);
        Assert.True(interval.Contains(interval.From));
        Assert.True(interval.Contains(interval.To));
        Assert.False(interval.Contains(interval.From.AddTicks(-1)));
        Assert.False(interval.Contains(interval.To.AddTicks(1)));
    }

    [Fact]
    public void Rejects_reversed_interval()
    {
        Assert.Throws<ArgumentException>(() => DocumentDiscoveryInterval.Create(
            Now.AddDays(1), Now, Now));
    }

    [Fact]
    public void Rejects_materially_future_end()
    {
        Assert.Throws<ArgumentException>(() => DocumentDiscoveryInterval.Create(
            Now, Now.AddMinutes(6), Now));
    }
}
