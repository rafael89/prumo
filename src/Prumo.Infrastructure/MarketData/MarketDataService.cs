using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prumo.Application.Abstractions;
using Prumo.Domain.Entities;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Infrastructure.MarketData;

public sealed class MarketDataService(PrumoDbContext db)
{
    public Task<AssetPrice?> GetLatestAsync(Guid assetId, CancellationToken ct = default)
        => db.AssetPrices.AsNoTracking().Where(x => x.AssetId == assetId).OrderByDescending(x => x.PriceDate).ThenByDescending(x => x.FetchedAt).FirstOrDefaultAsync(ct);

    public Task<AssetPrice?> GetAsOfAsync(Guid assetId, DateOnly date, CancellationToken ct = default)
        => db.AssetPrices.AsNoTracking().Where(x => x.AssetId == assetId && x.PriceDate <= date).OrderByDescending(x => x.PriceDate).ThenByDescending(x => x.FetchedAt).FirstOrDefaultAsync(ct);
}
