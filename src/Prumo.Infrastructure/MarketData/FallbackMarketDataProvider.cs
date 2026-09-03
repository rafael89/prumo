using Prumo.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Prumo.Infrastructure.MarketData;

public sealed class FallbackMarketDataProvider(IMarketDataProvider primary, IMarketDataProvider fallback, ILogger<FallbackMarketDataProvider> logger) : IMarketDataProvider
{
    public string Name => primary.Name;
    public async Task<IReadOnlyList<MarketPricePoint>> GetHistoryAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        try
        {
            var points = await primary.GetHistoryAsync(ticker, from, to, cancellationToken);
            if (points.Count > 0) return points.Select(x => x with { Source = primary.Name }).ToList();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested) { logger.LogWarning(exception, "Provider primário {Provider} falhou para {Ticker}; usando fallback {Fallback}.", primary.Name, ticker, fallback.Name); }
        var fallbackPoints = await fallback.GetHistoryAsync(ticker, from, to, cancellationToken);
        return fallbackPoints.Select(x => x with { Source = fallback.Name }).ToList();
    }
}
