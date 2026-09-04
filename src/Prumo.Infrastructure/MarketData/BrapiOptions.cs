namespace Prumo.Infrastructure.MarketData;

public sealed class BrapiOptions
{
    public string BaseUrl { get; set; } = "https://brapi.dev";
    public string? Token { get; set; }
    public int BackfillMonths { get; set; } = 12;
    public int MonthlyRequestLimit { get; set; } = 15000;
}

public sealed class BrapiMonthlyQuota
{
    private readonly object sync = new();
    private string month = CurrentMonth();
    private int used;

    public bool TryAcquire(int limit)
    {
        lock (sync)
        {
            var currentMonth = CurrentMonth();
            if (month != currentMonth) { month = currentMonth; used = 0; }
            if (used >= limit) return false;
            used++;
            return true;
        }
    }

    private static string CurrentMonth() => DateTime.UtcNow.ToString("yyyy-MM");
}
