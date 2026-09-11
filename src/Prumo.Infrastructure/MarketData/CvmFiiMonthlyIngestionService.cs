using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prumo.Domain.Entities;
using Prumo.Infrastructure.DocumentSources;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Infrastructure.MarketData;

public sealed class CvmFiiMonthlyIngestionService
{
    private readonly PrumoDbContext db;
    private readonly CvmFiiMonthlyStructuredSource source;
    private readonly ILogger<CvmFiiMonthlyIngestionService> logger;

    public CvmFiiMonthlyIngestionService(PrumoDbContext db, CvmFiiMonthlyStructuredSource source, ILogger<CvmFiiMonthlyIngestionService> logger)
    { this.db = db; this.source = source; this.logger = logger; }

    public async Task<int> IngestAsync(CancellationToken cancellationToken = default)
    {
        var assets = await db.Assets.Where(x => x.AssetType == Domain.Enums.AssetType.Fii && db.PortfolioPositions.Any(p => p.AssetId == x.Id && p.Quantity > 0)).ToListAsync(cancellationToken);
        var records = await source.FindAsync(assets.Select(x => new CvmFiiStructuredAsset(x.Ticker, x.Cnpj, x.AssetType)).ToArray(), cancellationToken);
        var known = await db.CvmFiiMonthlyObservations.Select(x => x.Fingerprint).ToHashSetAsync(cancellationToken);
        var byCnpj = assets.Where(x => !string.IsNullOrWhiteSpace(x.Cnpj)).ToDictionary(x => new string(x.Cnpj!.Where(char.IsDigit).ToArray()), StringComparer.Ordinal);
        foreach (var asset in assets.Where(x => string.IsNullOrWhiteSpace(x.Cnpj)))
            logger.LogWarning("CVM FII structured ingestion: asset identifier missing; ticker={Ticker}", asset.Ticker);
        var returnedCnpjs = records.Select(x => x.FundClassCnpj).ToHashSet(StringComparer.Ordinal);
        foreach (var asset in assets.Where(x => !string.IsNullOrWhiteSpace(x.Cnpj) && !returnedCnpjs.Contains(new string(x.Cnpj!.Where(char.IsDigit).ToArray()))))
            logger.LogInformation("CVM FII structured ingestion: no record for current dataset; ticker={Ticker}, cnpj={Cnpj}", asset.Ticker, asset.Cnpj);
        var added = 0;
        foreach (var record in records)
        {
            if (!byCnpj.TryGetValue(record.FundClassCnpj, out var asset) || known.Contains(record.Fingerprint)) continue;
            if (record.NetAssetValue is null) logger.LogWarning("CVM FII field absent: metric=NetAssetValue, cnpj={Cnpj}, referenceDate={ReferenceDate}", record.FundClassCnpj, record.ReferenceDate);
            if (record.NavPerShare is null) logger.LogWarning("CVM FII field absent: metric=NAVPerShare, cnpj={Cnpj}, referenceDate={ReferenceDate}", record.FundClassCnpj, record.ReferenceDate);
            if (record.Cash is null) logger.LogWarning("CVM FII field absent: metric=Cash, cnpj={Cnpj}, referenceDate={ReferenceDate}", record.FundClassCnpj, record.ReferenceDate);
            db.CvmFiiMonthlyObservations.Add(new CvmFiiMonthlyObservation(asset.Id, record.FundClassCnpj, record.ReferenceDate, record.SourceVersion, record.Fingerprint, record.SourceReference, record.SchemaVersion, DateTimeOffset.UtcNow, record.NetAssetValue, record.NavPerShare, record.Cash));
            known.Add(record.Fingerprint); added++;
        }
        if (added > 0) await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("CVM FII structured ingestion: assets={AssetCount}, records={RecordCount}, added={AddedCount}", assets.Count, records.Count, added);
        return added;
    }
}
