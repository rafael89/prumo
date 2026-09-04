using Microsoft.EntityFrameworkCore;
using Prumo.Application.Abstractions;
using Prumo.Domain.Entities;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Infrastructure.MarketData;

public sealed class ExternalAssetMaterializer(PrumoDbContext db)
{
    public async Task<Guid> MaterializeAsync(DiscoveredAsset candidate, CancellationToken cancellationToken = default)
    {
        if (candidate.Origin != CandidateOrigin.ExternalDiscovery || candidate.AssetClass is null || string.IsNullOrWhiteSpace(candidate.Ticker) || string.IsNullOrWhiteSpace(candidate.Name))
            throw new ArgumentException("Candidato externo não verificado.", nameof(candidate));
        var ticker = candidate.Ticker.Trim().ToUpperInvariant();
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({ticker}, 0))", cancellationToken);
        var existing = await db.Assets.SingleOrDefaultAsync(x => x.Ticker == ticker, cancellationToken);
        if (existing is not null) return existing.Id;
        var asset = new Asset(ticker, candidate.Name.Trim(), candidate.AssetType, candidate.Sector, candidate.AssetClass);
        if (!string.IsNullOrWhiteSpace(candidate.Exchange) && string.Equals(candidate.Exchange, "B3", StringComparison.OrdinalIgnoreCase)) asset.SetListingMetadata("BR", candidate.Currency);
        db.Assets.Add(asset);
        await db.SaveChangesAsync(cancellationToken);
        return asset.Id;
    }
}
