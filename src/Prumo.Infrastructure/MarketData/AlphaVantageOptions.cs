namespace Prumo.Infrastructure.MarketData;

public sealed class AlphaVantageOptions
{
    public string BaseUrl { get; set; } = "https://www.alphavantage.co/query";
    public string? ApiKey { get; set; }
    public string SymbolSuffix { get; set; } = ".SAO";
}
