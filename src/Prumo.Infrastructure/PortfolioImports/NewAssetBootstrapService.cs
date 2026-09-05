using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prumo.Application.Abstractions;
using Prumo.Application.Documents;
using Prumo.Application.PortfolioImports;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Infrastructure.PortfolioImports;

public sealed class NewAssetBootstrapService
{
    private readonly PrumoDbContext db;
    private readonly IDocumentSource source;
    private readonly ILogger<NewAssetBootstrapService> logger;
    private readonly DocumentBootstrapOptions options;
    public NewAssetBootstrapService(PrumoDbContext db, IDocumentSource source, ILogger<NewAssetBootstrapService> logger, IOptions<DocumentBootstrapOptions>? options = null) { this.db = db; this.source = source; this.logger = logger; this.options = options?.Value ?? new DocumentBootstrapOptions(); }

    public async Task DiscoverLatestOnlyAsync(IEnumerable<Guid> assetIds, CancellationToken ct)
    {
        var assets = await db.Assets.Where(a => assetIds.Contains(a.Id)
                && (a.AssetClass == AssetClass.Fii || a.AssetClass == AssetClass.BrazilianStock)
                && db.PortfolioPositions.Any(position => position.AssetId == a.Id && position.Quantity > 0))
            .ToListAsync(ct);
        if (assets.Count == 0) return;
        logger.LogInformation("CVM bootstrap iniciado para {AssetCount} novo(s) ativo(s)", assets.Count);
        var candidates = await source.FindAsync(assets.Select(a => new DocumentSourceAsset(a.Ticker, a.Cnpj, a.AssetClass)).ToArray(), ct);
        logger.LogInformation("CVM bootstrap descobriu {DocumentCount} candidato(s) para novos ativos", candidates.Count);
        foreach (var candidate in LatestDocumentSelector.SelectBootstrap(candidates, DateTimeOffset.UtcNow, options.MaterialFactLookbackMonths))
        {
            var asset = assets.Single(a => a.Ticker.Equals(candidate.Ticker, StringComparison.OrdinalIgnoreCase));
            var fingerprint = Document.CreateFingerprint(candidate.Ticker, candidate.PublishedAt, candidate.DocumentType, source.Name, candidate.SourceIdentifier, candidate.SourceUrl);
            var existing = await db.Documents.FirstOrDefaultAsync(d => d.Fingerprint == fingerprint || (d.AssetId == asset.Id && d.SourceName == source.Name && ((!string.IsNullOrWhiteSpace(candidate.SourceIdentifier) && d.SourceIdentifier == candidate.SourceIdentifier) || d.SourceUrl == candidate.SourceUrl)), ct);
            if (existing is not null)
            {
                existing.UpdateSourceMetadata(candidate.SourceCategory, candidate.SourceType, candidate.SourceSpecies, candidate.DocumentType);
                await RequeueIgnoredDocumentAsync(existing, ct);
                continue;
            }
            var document = new Document(asset.Id, candidate.DocumentType, candidate.PublishedAt, source.Name, candidate.SourceIdentifier, candidate.SourceUrl, fingerprint, candidate.DownloadUrl, candidate.SourceCategory, candidate.SourceType, candidate.SourceSpecies);
            var job = new ProcessingJob(document.Id, ProcessingJobType.DownloadDocument, DateTimeOffset.UtcNow);
            db.Documents.Add(document); db.ProcessingJobs.Add(job);
            logger.LogInformation("Portfolio import bootstrap scheduled: AssetId={AssetId}, Ticker={Ticker}, JobId={JobId}", asset.Id, asset.Ticker, job.Id);
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Bootstrap LatestOnly concluído para {AssetCount} ativo(s)", assets.Count);
    }

    private async Task RequeueIgnoredDocumentAsync(Document document, CancellationToken ct)
    {
        if (document.ProcessingStatus != DocumentProcessingStatus.Ignored || await db.DocumentAnalyses.AnyAsync(analysis => analysis.DocumentId == document.Id, ct)) return;
        if (await db.ProcessingJobs.AnyAsync(job => job.DocumentId == document.Id && (job.Status == ProcessingJobStatus.Queued || job.Status == ProcessingJobStatus.Processing), ct)) return;
        if (string.IsNullOrWhiteSpace(document.FilePath))
        {
            document.MarkPendingDownload();
            db.ProcessingJobs.Add(new ProcessingJob(document.Id, ProcessingJobType.DownloadDocument, DateTimeOffset.UtcNow));
        }
        else
        {
            document.MarkWaitingProcessing();
            db.ProcessingJobs.Add(new ProcessingJob(document.Id, ProcessingJobType.AnalyzeDocument, DateTimeOffset.UtcNow));
        }
    }

    public async Task<BootstrapRepairResult> EnsurePortfolioDocumentBootstrapAsync(Guid portfolioId, CancellationToken ct)
    {
        var assets = await db.PortfolioPositions.Where(p => p.PortfolioId == portfolioId && p.Quantity > 0)
            .Select(p => p.Asset!).Where(a => (a.AssetClass == AssetClass.Fii || a.AssetClass == AssetClass.BrazilianStock)).Distinct().ToListAsync(ct);
        var candidates = new List<Guid>();
        var skipped = 0;
        foreach (var asset in assets)
        {
            var covered = await db.Documents.AnyAsync(d => d.AssetId == asset.Id, ct) ||
                await db.ProcessingJobs.AnyAsync(j => j.Document!.AssetId == asset.Id && (j.Status == ProcessingJobStatus.Queued || j.Status == ProcessingJobStatus.Processing), ct);
            if (covered) { skipped++; continue; }
            candidates.Add(asset.Id);
        }
        await DiscoverLatestOnlyAsync(candidates, ct);
        logger.LogInformation("Portfolio bootstrap repair concluído: PortfolioId={PortfolioId}, BootstrapCandidateCount={BootstrapCandidateCount}, BootstrapSkippedExistingCoverageCount={BootstrapSkippedExistingCoverageCount}", portfolioId, candidates.Count, skipped);
        return new BootstrapRepairResult(assets.Count, candidates.Count, skipped);
    }
}

public sealed record BootstrapRepairResult(int AssetCount, int BootstrapCandidateCount, int BootstrapSkippedExistingCoverageCount);
