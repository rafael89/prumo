using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Prumo.Application.Abstractions;
using Prumo.Domain.Enums;

namespace Prumo.Infrastructure.MarketData;

public sealed class BrapiFinancialDataProvider(HttpClient http, IOptions<BrapiOptions> options, BrapiMonthlyQuota quota) : IFinancialDataProvider
{
    private readonly BrapiOptions settings = options.Value;
    public string Name => "Brapi";

    public async Task<FinancialDataResult?> GetAsync(FinancialDataRequest request, CancellationToken cancellationToken = default)
    {
        if (request.AssetType != AssetType.Stock || !quota.TryAcquire(settings.MonthlyRequestLimit)) return null;
        var ticker = request.Ticker.Trim().ToUpperInvariant();
        var url = $"{settings.BaseUrl.TrimEnd('/')}/api/quote/{Uri.EscapeDataString(ticker)}?modules=defaultKeyStatistics,financialData";
        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(settings.Token)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        using var response = await http.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var result = document.RootElement.TryGetProperty("results", out var results) ? results.EnumerateArray().FirstOrDefault() : default;
        if (result.ValueKind == JsonValueKind.Undefined || !MatchesTicker(result, ticker)) return null;
        var data = result.TryGetProperty("data", out var nested) ? nested : result;
        var statistics = data.TryGetProperty("defaultKeyStatistics", out var stats) ? stats : default;
        var financial = data.TryGetProperty("financialData", out var facts) ? facts : default;
        var referenceDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        return new(request.AssetId, ticker, request.AssetType, referenceDate, FinancialPeriodType.Ttm, FinancialDataSource.Provider, DateTimeOffset.UtcNow, url,
            Read(statistics, "returnOnEquity"), Read(financial, "profitMargins"), Read(statistics, "trailingPE"), Read(statistics, "priceToBook"), Read(statistics, "enterpriseToEbitda"), Read(statistics, "dividendYield"));
    }

    private static bool MatchesTicker(JsonElement result, string ticker)
        => result.TryGetProperty("symbol", out var symbol) && string.Equals(symbol.GetString()?.Trim().ToUpperInvariant(), ticker, StringComparison.Ordinal);

    private static decimal? Read(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("raw", out var raw)) value = raw;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        return decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number) ? number : null;
    }
}
