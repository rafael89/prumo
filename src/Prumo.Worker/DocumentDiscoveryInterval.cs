namespace Prumo.Worker;

public readonly record struct DocumentDiscoveryInterval(DateTimeOffset From, DateTimeOffset To)
{
    public static DocumentDiscoveryInterval Create(DateTimeOffset from, DateTimeOffset to, DateTimeOffset? now = null)
    {
        var normalizedFrom = from.ToUniversalTime();
        var normalizedTo = to.ToUniversalTime();
        if (normalizedFrom > normalizedTo) throw new ArgumentException("Discovery interval requires from <= to.");
        if (normalizedTo > (now ?? DateTimeOffset.UtcNow).AddMinutes(5)) throw new ArgumentException("Discovery interval cannot end materially in the future.");
        return new(normalizedFrom, normalizedTo);
    }

    public bool Contains(DateTimeOffset publishedAt)
    {
        var value = publishedAt.ToUniversalTime();
        return value >= From && value <= To;
    }
}
