using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Prumo.Application.Abstractions;

namespace Prumo.Infrastructure.MarketData;

public sealed class BrapiMarketDataProvider(HttpClient http, IOptions<BrapiOptions> options) : IMarketDataProvider
{
    private readonly BrapiOptions settings = options.Value;
    public string Name => "Brapi";

    public async Task<IReadOnlyList<MarketPricePoint>> GetHistoryAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var url = $"{settings.BaseUrl.TrimEnd('/')}/api/quote/{Uri.EscapeDataString(ticker)}?range={Math.Max(1, (to.DayNumber - from.DayNumber) / 365 + 1)}y&interval=1d";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(settings.Token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var result = document.RootElement.GetProperty("results").EnumerateArray().FirstOrDefault();
        if (result.ValueKind == JsonValueKind.Undefined) return [];
        var data = result.TryGetProperty("data", out var nestedData) ? nestedData : result;
        if (!data.TryGetProperty("historicalDataPrice", out var history)) return [];
        var currency = data.TryGetProperty("currency", out var currencyValue) ? currencyValue.GetString() ?? "BRL" : "BRL";
        var points = new List<MarketPricePoint>();
        foreach (var item in history.EnumerateArray())
        {
            if (!item.TryGetProperty("date", out var timestamp) || !item.TryGetProperty("close", out var close) || !close.TryGetDecimal(out var value) || value <= 0) continue;
            var date = DateTimeOffset.FromUnixTimeSeconds(timestamp.GetInt64()).UtcDateTime.Date;
            var priceDate = DateOnly.FromDateTime(date);
            if (priceDate >= from && priceDate <= to) points.Add(new(priceDate, value, currency, Name));
        }
        return points;
    }
}
