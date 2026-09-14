using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prumo.Domain.Entities;
using Prumo.Infrastructure.DocumentSources;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Infrastructure.MarketData;

public sealed record CvmFiiMonthlyIngestionResult(int EligibleAssets, int AssetsWithCnpj, int MatchedAssets, int RecordsRead, int ObservationsInserted, int ObservationsSkipped);

public sealed class CvmFiiMonthlyIngestionService
{
    private readonly PrumoDbContext db;
    private readonly CvmFiiMonthlyStructuredSource source;
    private readonly ILogger<CvmFiiMonthlyIngestionService> logger;

    public CvmFiiMonthlyIngestionService(PrumoDbContext db, CvmFiiMonthlyStructuredSource source, ILogger<CvmFiiMonthlyIngestionService> logger)
    { this.db = db; this.source = source; this.logger = logger; }

    public async Task<CvmFiiMonthlyIngestionResult> IngestAsync(CancellationToken cancellationToken = default)
    {
        var assets = await db.Assets.Where(x => x.AssetType == Domain.Enums.AssetType.Fii && db.PortfolioPositions.Any(p => p.AssetId == x.Id && p.Quantity > 0)).ToListAsync(cancellationToken);
        var assetsWithCnpj = assets.Count(x => !string.IsNullOrWhiteSpace(x.Cnpj));
        IReadOnlyList<CvmFiiMonthlyRecord> records;
        try
        {
            records = await source.FindAsync(assets.Select(x => new CvmFiiStructuredAsset(x.Ticker, x.Cnpj, x.AssetType)).ToArray(), cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "CVM FII structured ingestion download/parse failure: sourceUrl={SourceUrl}", source.Diagnostics.Url);
            throw;
        }
        var known = await db.CvmFiiMonthlyObservations.Select(x => x.Fingerprint).ToHashSetAsync(cancellationToken);
        var byCnpj = assets.Where(x => !string.IsNullOrWhiteSpace(x.Cnpj)).ToDictionary(x => new string(x.Cnpj!.Where(char.IsDigit).ToArray()), StringComparer.Ordinal);
        foreach (var asset in assets.Where(x => string.IsNullOrWhiteSpace(x.Cnpj)))
            logger.LogWarning("CVM FII structured ingestion: asset identifier missing; ticker={Ticker}", asset.Ticker);
        var returnedCnpjs = records.Select(x => x.FundClassCnpj).ToHashSet(StringComparer.Ordinal);
        foreach (var asset in assets.Where(x => !string.IsNullOrWhiteSpace(x.Cnpj) && !returnedCnpjs.Contains(new string(x.Cnpj!.Where(char.IsDigit).ToArray()))))
            logger.LogInformation("CVM FII structured ingestion: no record for current dataset; ticker={Ticker}, cnpj={Cnpj}", asset.Ticker, asset.Cnpj);
        var added = 0; var skipped = 0;
        foreach (var record in records)
        {
            if (!byCnpj.TryGetValue(record.FundClassCnpj, out var asset)) continue;
            if (known.Contains(record.Fingerprint)) { skipped++; continue; }
            if (record.NetAssetValue is null) logger.LogWarning("CVM FII field absent: metric=NetAssetValue, cnpj={Cnpj}, referenceDate={ReferenceDate}", record.FundClassCnpj, record.ReferenceDate);
            if (record.NavPerShare is null) logger.LogWarning("CVM FII field absent: metric=NAVPerShare, cnpj={Cnpj}, referenceDate={ReferenceDate}", record.FundClassCnpj, record.ReferenceDate);
            if (record.Cash is null) logger.LogWarning("CVM FII field absent: metric=Cash, cnpj={Cnpj}, referenceDate={ReferenceDate}", record.FundClassCnpj, record.ReferenceDate);
            db.CvmFiiMonthlyObservations.Add(new CvmFiiMonthlyObservation(asset.Id, record.FundClassCnpj, record.ReferenceDate, record.SourceVersion, record.Fingerprint, record.SourceReference, record.SchemaVersion, DateTimeOffset.UtcNow, record.NetAssetValue, record.NavPerShare, record.Cash));
            known.Add(record.Fingerprint); added++;
        }
        if (added > 0) await db.SaveChangesAsync(cancellationToken);
        var matchedAssets = records.Select(x => x.FundClassCnpj).Where(byCnpj.ContainsKey).Distinct(StringComparer.Ordinal).Count();
        logger.LogInformation("CVM FII structured ingestion: assets={AssetCount}, assetsWithCnpj={AssetsWithCnpj}, rawRows={RawRows}, records={RecordCount}, matchedAssets={MatchedAssets}, added={AddedCount}, skipped={SkippedCount}", assets.Count, assetsWithCnpj, source.Diagnostics.RawRows, records.Count, matchedAssets, added, skipped);
        return new(assets.Count, assetsWithCnpj, matchedAssets, records.Count, added, skipped);
    }
}
