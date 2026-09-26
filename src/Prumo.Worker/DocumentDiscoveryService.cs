using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Prumo.Application.Abstractions;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.MarketData;
using Prumo.Infrastructure.Persistence;
using Prumo.Application.Documents;

namespace Prumo.Worker;

public sealed record DocumentDiscoveryRunSummary(
    DateTimeOffset From,
    DateTimeOffset To,
    int EligibleAssets,
    int CandidatesReturned,
    int NewDocuments,
    int ExistingDocuments,
    int Skipped,
    int Failed,
    TimeSpan Duration);

public sealed class DocumentDiscoveryService
{
    private readonly PrumoDbContext dbContext;
    private readonly IDocumentSource source;
    private readonly CvmFiiCnpjEnrichmentService cnpjEnrichment;
    private readonly ILogger<DocumentDiscoveryService> logger;

    public DocumentDiscoveryService(
        PrumoDbContext dbContext,
        IDocumentSource source,
        CvmFiiCnpjEnrichmentService cnpjEnrichment,
        ILogger<DocumentDiscoveryService> logger)
    {
        this.dbContext = dbContext;
        this.source = source;
        this.cnpjEnrichment = cnpjEnrichment;
        this.logger = logger;
    }

    public async Task<DocumentDiscoveryRunSummary> DiscoverAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var interval = DocumentDiscoveryInterval.Create(from, to);
        var normalizedFrom = interval.From;
        var normalizedTo = interval.To;

        var startedAt = Stopwatch.GetTimestamp();
        var usesRelationalDatabase = dbContext.Database.IsRelational();
        if (usesRelationalDatabase) await dbContext.Database.OpenConnectionAsync(cancellationToken);
        if (usesRelationalDatabase && !await TryAcquireDiscoveryLockAsync(cancellationToken))
        {
            await dbContext.Database.CloseConnectionAsync();
            logger.LogInformation("Discovery interval ignored: another instance is already running.");
            return new(normalizedFrom, normalizedTo, 0, 0, 0, 0, 0, 0, Stopwatch.GetElapsedTime(startedAt));
        }

        try
        {
            try
            {
                await cnpjEnrichment.EnrichAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "FII CNPJ enrichment failed before discovery; using persisted identifiers.");
            }

            var assets = await dbContext.PortfolioPositions
                .Include(position => position.Asset)
                .Where(position => position.Asset != null && position.Quantity > 0)
                .Select(position => position.Asset!)
                .Distinct()
                .ToListAsync(cancellationToken);

            logger.LogInformation(
                "Document discovery started: from={From}; to={To}; eligibleAssets={EligibleAssets}; tickers={Tickers}",
                normalizedFrom, normalizedTo, assets.Count, string.Join(", ", assets.Select(asset => asset.Ticker)));

            if (assets.Count == 0)
            {
                return Complete(normalizedFrom, normalizedTo, assets.Count, 0, 0, 0, 0, 0, startedAt);
            }

            IReadOnlyList<DocumentCandidate> candidates;
            try
            {
                candidates = await source.FindAsync(
                    assets.Select(asset => new DocumentSourceAsset(asset.Ticker, asset.Cnpj, asset.AssetClass)).ToArray(),
                    normalizedFrom,
                    normalizedTo,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Document discovery source {SourceName} failed for interval {From}..{To}.", source.Name, normalizedFrom, normalizedTo);
                throw;
            }

            var assetByTicker = assets.ToDictionary(asset => asset.Ticker, StringComparer.OrdinalIgnoreCase);
            var reclassifiedDocuments = 0;
            foreach (var candidate in candidates)
            {
                if (!assetByTicker.TryGetValue(candidate.Ticker, out var asset)) continue;
                var existing = await dbContext.Documents.FirstOrDefaultAsync(document =>
                    document.AssetId == asset.Id && document.SourceName == source.Name
                    && ((!string.IsNullOrWhiteSpace(candidate.SourceIdentifier) && document.SourceIdentifier == candidate.SourceIdentifier)
                        || document.SourceUrl == candidate.SourceUrl), cancellationToken);
                if (existing is not null)
                {
                    existing.UpdateSourceMetadata(candidate.SourceCategory, candidate.SourceType, candidate.SourceSpecies, candidate.DocumentType, candidate.DownloadUrl);
                    reclassifiedDocuments++;
                }
            }
            if (reclassifiedDocuments > 0) await dbContext.SaveChangesAsync(cancellationToken);

            var inInterval = candidates
                .Where(candidate => interval.Contains(candidate.PublishedAt))
                .ToList();
            var skipped = candidates.Count - inInterval.Count;
            var existingDocuments = await dbContext.Documents
                .Where(document => document.SourceName == source.Name && assets.Select(asset => asset.Id).Contains(document.AssetId))
                .Select(document => new { document.AssetId, document.Fingerprint, document.SourceIdentifier, document.SourceUrl })
                .ToListAsync(cancellationToken);
            var existingCount = 0;
            var failed = 0;
            var newDocuments = 0;

            foreach (var candidate in inInterval)
            {
                if (!assetByTicker.TryGetValue(candidate.Ticker, out var asset))
                {
                    failed++;
                    logger.LogWarning("Document candidate skipped: ticker {Ticker} is not registered in Assets.", candidate.Ticker);
                    continue;
                }

                var fingerprint = Document.CreateFingerprint(candidate.Ticker, candidate.PublishedAt, candidate.DocumentType, source.Name, candidate.SourceIdentifier, candidate.SourceUrl);
                var exists = existingDocuments.Any(document =>
                    document.AssetId == asset.Id
                    && (document.Fingerprint == fingerprint
                        || (!string.IsNullOrWhiteSpace(candidate.SourceIdentifier) && document.SourceIdentifier == candidate.SourceIdentifier)
                        || document.SourceUrl == candidate.SourceUrl));
                if (exists)
                {
                    existingCount++;
                    skipped++;
                    logger.LogInformation("Document candidate already exists: ticker={Ticker}; fingerprint={Fingerprint}", candidate.Ticker, fingerprint);
                    continue;
                }

                var document = new Document(asset.Id, candidate.DocumentType, candidate.PublishedAt, source.Name, candidate.SourceIdentifier, candidate.SourceUrl, fingerprint, candidate.DownloadUrl, candidate.SourceCategory, candidate.SourceType, candidate.SourceSpecies);
                dbContext.Documents.Add(document);
                if (DocumentClassificationPolicy.IsOperationalIssuanceDocument(candidate.SourceCategory, candidate.SourceType, candidate.SourceSpecies))
                {
                    document.MarkIgnored("Documento operacional de emissão; mantido apenas como histórico e não enviado para análise.");
                    logger.LogInformation("Documento operacional de emissão mantido apenas como histórico: ticker={Ticker}; tipo={SourceType}; identificador={SourceIdentifier}", candidate.Ticker, candidate.SourceType, candidate.SourceIdentifier);
                }
                else
                {
                    dbContext.ProcessingJobs.Add(new ProcessingJob(document.Id, ProcessingJobType.DownloadDocument, DateTimeOffset.UtcNow));
                }
                await dbContext.SaveChangesAsync(cancellationToken);
                existingDocuments.Add(new { AssetId = asset.Id, Fingerprint = fingerprint, SourceIdentifier = candidate.SourceIdentifier, SourceUrl = candidate.SourceUrl });
                newDocuments++;
                logger.LogInformation("Document {DocumentId} discovered and download job queued.", document.Id);
            }

            return Complete(normalizedFrom, normalizedTo, assets.Count, candidates.Count, newDocuments, existingCount, skipped, failed, startedAt);
        }
        finally
        {
            try
            {
                if (usesRelationalDatabase) await ReleaseDiscoveryLockAsync(CancellationToken.None);
            }
            finally
            {
                if (usesRelationalDatabase) await dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    private DocumentDiscoveryRunSummary Complete(DateTimeOffset from, DateTimeOffset to, int eligibleAssets, int candidates, int newDocuments, int existingDocuments, int skipped, int failed, long startedAt)
    {
        var summary = new DocumentDiscoveryRunSummary(from, to, eligibleAssets, candidates, newDocuments, existingDocuments, skipped, failed, Stopwatch.GetElapsedTime(startedAt));
        logger.LogInformation(
            "Document discovery completed: from={From}; to={To}; eligibleAssets={EligibleAssets}; candidatesReturned={CandidatesReturned}; newDocuments={NewDocuments}; existingDocuments={ExistingDocuments}; skipped={Skipped}; failed={Failed}; durationMs={DurationMs}",
            summary.From, summary.To, summary.EligibleAssets, summary.CandidatesReturned, summary.NewDocuments, summary.ExistingDocuments, summary.Skipped, summary.Failed, summary.Duration.TotalMilliseconds);
        return summary;
    }

    private async Task<bool> TryAcquireDiscoveryLockAsync(CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(hashtext('prumo:document-discovery'))";
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task ReleaseDiscoveryLockAsync(CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_advisory_unlock(hashtext('prumo:document-discovery'))";
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
