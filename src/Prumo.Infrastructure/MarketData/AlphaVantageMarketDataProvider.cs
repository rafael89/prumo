using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Prumo.Application.Abstractions;

namespace Prumo.Infrastructure.MarketData;

public sealed class AlphaVantageMarketDataProvider(HttpClient http, IOptions<AlphaVantageOptions> options) : IMarketDataProvider
{
    private readonly AlphaVantageOptions settings = options.Value;
    public string Name => "AlphaVantage";

    public async Task<IReadOnlyList<MarketPricePoint>> GetHistoryAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) throw new InvalidOperationException("MarketData:AlphaVantage:ApiKey não configurada.");
        var symbol = $"{ticker.Trim().ToUpperInvariant()}{settings.SymbolSuffix}";
        var url = $"{settings.BaseUrl}?function=TIME_SERIES_DAILY&symbol={Uri.EscapeDataString(symbol)}&outputsize=compact&apikey={Uri.EscapeDataString(settings.ApiKey)}";
        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        if (document.RootElement.TryGetProperty("Note", out var note)) throw new InvalidOperationException(note.GetString() ?? "Alpha Vantage rate limit atingido.");
        if (document.RootElement.TryGetProperty("Error Message", out var error)) throw new InvalidOperationException(error.GetString() ?? "Alpha Vantage rejeitou o ativo.");
        if (!document.RootElement.TryGetProperty("Time Series (Daily)", out var series)) return [];
        return series.EnumerateObject().Select(item => DateOnly.TryParseExact(item.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date >= from && date <= to && item.Value.TryGetProperty("4. close", out var close) && TryReadDecimal(close, out var value) && value > 0 ? new MarketPricePoint(date, value, "BRL", Name) : null).Where(x => x is not null).Select(x => x!).ToList();
    }

    private static bool TryReadDecimal(JsonElement value, out decimal result)
        => value.ValueKind == JsonValueKind.Number ? value.TryGetDecimal(out result) : decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
}
