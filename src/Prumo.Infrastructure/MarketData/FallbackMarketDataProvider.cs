using Prumo.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Prumo.Domain.Enums;

namespace Prumo.Infrastructure.MarketData;

public sealed class FallbackMarketDataProvider(IMarketDataProvider primary, IMarketDataProvider fallback, ILogger<FallbackMarketDataProvider> logger) : IMarketDataProvider
{
    private static int fallbackUnavailable;
    public string Name => primary.Name;
    public async Task<IReadOnlyList<MarketPricePoint>> GetHistoryAsync(string ticker, DateOnly from, DateOnly to, AssetType assetType = AssetType.Stock, CancellationToken cancellationToken = default)
    {
        try
        {
            var points = await primary.GetHistoryAsync(ticker, from, to, assetType, cancellationToken);
            if (points.Count > 0) return points.Select(x => x with { Source = primary.Name }).ToList();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested) { logger.LogWarning(exception, "Provider primário {Provider} falhou para {Ticker}; usando fallback {Fallback}.", primary.Name, ticker, fallback.Name); }
        if (Volatile.Read(ref fallbackUnavailable) == 1) return [];
        try
        {
            var fallbackPoints = await fallback.GetHistoryAsync(ticker, from, to, assetType, cancellationToken);
            return fallbackPoints.Select(x => x with { Source = fallback.Name }).ToList();
        }
        catch (AlphaVantageRateLimitException exception) when (!cancellationToken.IsCancellationRequested)
        {
            Interlocked.Exchange(ref fallbackUnavailable, 1);
            logger.LogWarning(exception, "Fallback {Fallback} atingiu o limite; chamadas adicionais foram suspensas até reiniciar o worker.", fallback.Name);
            return [];
        }
    }
}
