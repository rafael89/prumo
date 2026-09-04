using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prumo.Application.Abstractions;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Infrastructure.MarketData;

public sealed class FinancialDataRefreshService(PrumoDbContext db, IFinancialDataProvider provider, ILogger<FinancialDataRefreshService> logger)
{
    private static readonly TimeSpan FundamentalFreshness = TimeSpan.FromDays(400);
    private static readonly TimeSpan ValuationFreshness = TimeSpan.FromDays(35);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var assets = await db.Assets.Where(x => x.IsActive && x.AssetType == AssetType.Stock).ToListAsync(cancellationToken);
        foreach (var asset in assets)
        {
            var hasFreshFundamentals = await db.AssetFundamentalSnapshots.AnyAsync(x => x.AssetId == asset.Id && now - x.ObservedAt <= FundamentalFreshness, cancellationToken);
            var hasFreshValuation = await db.AssetValuationSnapshots.AnyAsync(x => x.AssetId == asset.Id && now - x.ObservedAt <= ValuationFreshness, cancellationToken);
            if (hasFreshFundamentals && hasFreshValuation) continue;
            try
            {
                var result = await provider.GetAsync(new(asset.Id, asset.Ticker, asset.AssetType), cancellationToken);
                if (result is null || result.AssetId != asset.Id || !string.Equals(result.Ticker, asset.Ticker.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                if (!hasFreshFundamentals && (result.Roe.HasValue || result.NetMargin.HasValue)) UpsertFundamentals(asset.Id, result);
                if (!hasFreshValuation && (result.PriceEarnings.HasValue || result.PriceBook.HasValue || result.EvEbitda.HasValue || result.DividendYield.HasValue)) UpsertValuation(asset.Id, result);
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Falha ao atualizar dados financeiros de {Ticker} via {Provider}; snapshots anteriores preservados.", asset.Ticker, provider.Name);
            }
        }
    }

    private void UpsertFundamentals(Guid assetId, FinancialDataResult result)
    {
        var current = db.AssetFundamentalSnapshots.Local.FirstOrDefault(x => x.AssetId == assetId && x.Source == result.Source && x.ReferenceDate == result.ReferenceDate && x.PeriodType == result.PeriodType)
            ?? db.AssetFundamentalSnapshots.FirstOrDefault(x => x.AssetId == assetId && x.Source == result.Source && x.ReferenceDate == result.ReferenceDate && x.PeriodType == result.PeriodType);
        if (current is null) db.AssetFundamentalSnapshots.Add(new(assetId, result.ReferenceDate, result.PeriodType, result.Source, result.SourceReference, result.ObservedAt, roe: result.Roe, netMargin: result.NetMargin));
        else current.ReplaceValues(result.ObservedAt, result.SourceReference, result.Roe, result.NetMargin);
    }

    private void UpsertValuation(Guid assetId, FinancialDataResult result)
    {
        var current = db.AssetValuationSnapshots.Local.FirstOrDefault(x => x.AssetId == assetId && x.Source == result.Source && x.ReferenceDate == result.ReferenceDate)
            ?? db.AssetValuationSnapshots.FirstOrDefault(x => x.AssetId == assetId && x.Source == result.Source && x.ReferenceDate == result.ReferenceDate);
        if (current is null) db.AssetValuationSnapshots.Add(new(assetId, result.ReferenceDate, result.Source, result.SourceReference, result.ObservedAt, result.PriceEarnings, result.PriceBook, result.EvEbitda, result.DividendYield));
        else current.ReplaceValues(result.ObservedAt, result.SourceReference, result.PriceEarnings, result.PriceBook, result.EvEbitda, result.DividendYield);
    }
}
