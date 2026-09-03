namespace Prumo.Infrastructure.MarketData;

public sealed class BrapiOptions
{
    public string BaseUrl { get; set; } = "https://brapi.dev";
    public string? Token { get; set; }
    public int BackfillMonths { get; set; } = 12;
}
