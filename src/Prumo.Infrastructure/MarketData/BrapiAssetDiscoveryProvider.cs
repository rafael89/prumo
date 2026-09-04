using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Prumo.Application.Abstractions;
using Prumo.Domain.Enums;

namespace Prumo.Infrastructure.MarketData;

public sealed class BrapiAssetDiscoveryProvider(HttpClient http, IOptions<BrapiOptions> options, BrapiMonthlyQuota quota) : IAssetDiscoveryProvider
{
    public const int RawLimit = 20;
    public const int VerifiedLimit = 5;
    private readonly BrapiOptions settings = options.Value;
    public string Name => "Brapi";

    public async Task<IReadOnlyList<DiscoveredAsset>> DiscoverAsync(AssetDiscoveryRequest request, CancellationToken cancellationToken = default)
    {
        var type = request.TargetClass switch { AssetClass.BrazilianStock => "stock", AssetClass.Fii => "fund", _ => null };
        if (type is null || request.MaxResults <= 0 || !quota.TryAcquire(settings.MonthlyRequestLimit)) return [];
        var limit = Math.Min(RawLimit, Math.Max(VerifiedLimit, request.MaxResults));
        var url = $"{settings.BaseUrl.TrimEnd('/')}/api/quote/list?type={type}&limit={limit}&sortBy=name&sortOrder=asc";
        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(settings.Token)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        using var response = await http.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("stocks", out var stocks)) return [];
        var known = request.KnownTickers.Select(NormalizeTicker).ToHashSet(StringComparer.Ordinal);
        var candidates = stocks.EnumerateArray()
            .Select(x => Map(x, request.TargetClass))
            .Where(x => x is not null && !known.Contains(x.Ticker))
            .Select(x => x!)
            .GroupBy(x => x.Ticker, StringComparer.Ordinal)
            .Select(x => x.First())
            .OrderBy(x => x.Ticker, StringComparer.Ordinal)
            .Take(Math.Min(request.MaxResults, VerifiedLimit))
            .ToArray();
        var verified = new List<DiscoveredAsset>();
        foreach (var candidate in candidates)
        {
            if (!quota.TryAcquire(settings.MonthlyRequestLimit)) break;
            var exact = await VerifyAsync(candidate.Ticker, candidate.AssetType, cancellationToken);
            if (exact is not null && string.Equals(exact.Ticker, candidate.Ticker, StringComparison.Ordinal)) verified.Add(candidate with { SourceReference = exact.SourceReference });
        }
        return verified;
    }

    private async Task<DiscoveredAsset?> VerifyAsync(string ticker, AssetType assetType, CancellationToken cancellationToken)
    {
        var url = $"{settings.BaseUrl.TrimEnd('/')}/api/quote/{Uri.EscapeDataString(ticker)}";
        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(settings.Token)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        using var response = await http.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var result = document.RootElement.TryGetProperty("results", out var results) ? results.EnumerateArray().FirstOrDefault() : default;
        if (result.ValueKind == JsonValueKind.Undefined || !result.TryGetProperty("symbol", out var symbol) || !string.Equals(NormalizeTicker(symbol.GetString()), ticker, StringComparison.Ordinal)) return null;
        return new(Name, ticker, ticker, assetType, assetType == AssetType.Fii ? AssetClass.Fii : AssetClass.BrazilianStock, "B3", "BRL", null, url);
    }

    private static DiscoveredAsset? Map(JsonElement item, AssetClass targetClass)
    {
        if (!item.TryGetProperty("stock", out var stock) || !item.TryGetProperty("name", out var name)) return null;
        var ticker = NormalizeTicker(stock.GetString());
        if (ticker.Length == 0) return null;
        var assetType = targetClass == AssetClass.Fii ? AssetType.Fii : AssetType.Stock;
        return new("Brapi", ticker, name.GetString()?.Trim() ?? ticker, assetType, targetClass, "B3", "BRL", item.TryGetProperty("sector", out var sector) ? sector.GetString() : null, null);
    }

    public static string NormalizeTicker(string? ticker) => (ticker ?? string.Empty).Trim().ToUpperInvariant();
}
