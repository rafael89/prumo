using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prumo.Application.Abstractions;
using Prumo.Domain.Entities;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Infrastructure.MarketData;

public sealed class MarketDataService(PrumoDbContext db, IMarketDataProvider provider, ILogger<MarketDataService> logger)
{
    public async Task<IReadOnlyList<AssetPrice>> RefreshAsync(CancellationToken ct = default)
    {
        var assets = await db.Assets.Where(x => x.IsActive && (x.AssetType == Domain.Enums.AssetType.Stock || x.AssetType == Domain.Enums.AssetType.Fii || x.AssetType == Domain.Enums.AssetType.Etf)).ToListAsync(ct);
        var refreshed = new List<AssetPrice>();
        foreach (var asset in assets)
        {
            try { refreshed.AddRange(await RefreshAsync(asset, ct)); }
            catch (Exception exception) when (!ct.IsCancellationRequested) { logger.LogWarning(exception, "Falha ao atualizar market data de {Ticker}; dados persistidos foram preservados.", asset.Ticker); }
        }
        return refreshed;
    }

    public async Task<IReadOnlyList<AssetPrice>> RefreshAsync(Asset asset, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var firstTransaction = await db.PortfolioTransactionItems.Where(x => x.AssetId == asset.Id).Select(x => (DateTimeOffset?)x.PortfolioTransaction!.OccurredAt).MinAsync(ct);
        var from = firstTransaction.HasValue ? DateOnly.FromDateTime(firstTransaction.Value.UtcDateTime.Date) : today.AddMonths(-12);
        var points = await provider.GetHistoryAsync(asset.Ticker, from, today, ct);
        var existing = await db.AssetPrices.Where(x => x.AssetId == asset.Id && x.PriceDate >= from && x.PriceDate <= today).ToDictionaryAsync(x => (x.PriceDate, x.Source), ct);
        foreach (var point in points.Where(x => x.Close > 0))
        {
            var source = point.Source ?? provider.Name;
            if (existing.TryGetValue((point.PriceDate, source), out var current)) current.Correct(point.Close, DateTimeOffset.UtcNow);
            else db.AssetPrices.Add(new AssetPrice(asset.Id, point.PriceDate, point.Close, point.Currency, source, DateTimeOffset.UtcNow));
        }
        await db.SaveChangesAsync(ct);
        return points.Select(point => existing.GetValueOrDefault((point.PriceDate, point.Source ?? provider.Name)) ?? new AssetPrice(asset.Id, point.PriceDate, point.Close, point.Currency, point.Source ?? provider.Name, DateTimeOffset.UtcNow)).ToList();
    }

    public Task<AssetPrice?> GetLatestAsync(Guid assetId, CancellationToken ct = default)
        => db.AssetPrices.AsNoTracking().Where(x => x.AssetId == assetId).OrderByDescending(x => x.PriceDate).ThenByDescending(x => x.FetchedAt).FirstOrDefaultAsync(ct);

    public Task<AssetPrice?> GetAsOfAsync(Guid assetId, DateOnly date, CancellationToken ct = default)
        => db.AssetPrices.AsNoTracking().Where(x => x.AssetId == assetId && x.PriceDate <= date).OrderByDescending(x => x.PriceDate).ThenByDescending(x => x.FetchedAt).FirstOrDefaultAsync(ct);
}
