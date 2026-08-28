using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Lodira.Application.Abstractions;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Infrastructure.Persistence;

namespace Lodira.Infrastructure.PortfolioImports;

public sealed class NewAssetBootstrapService
{
    private readonly LodiraDbContext db;
    private readonly IDocumentSource source;
    private readonly ILogger<NewAssetBootstrapService> logger;
    public NewAssetBootstrapService(LodiraDbContext db, IDocumentSource source, ILogger<NewAssetBootstrapService> logger) { this.db = db; this.source = source; this.logger = logger; }

    public async Task DiscoverLatestOnlyAsync(IEnumerable<Guid> assetIds, CancellationToken ct)
    {
        var assets = await db.Assets.Where(a => assetIds.Contains(a.Id) && a.AssetClass == AssetClass.Fii && a.Cnpj != null).ToListAsync(ct);
        if (assets.Count == 0) return;
        logger.LogInformation("CVM bootstrap iniciado para {AssetCount} novo(s) FII", assets.Count);
        var candidates = await source.FindAsync(assets.Select(a => new DocumentSourceAsset(a.Ticker, a.Cnpj)).ToArray(), ct);
        logger.LogInformation("CVM bootstrap descobriu {DocumentCount} candidato(s) para novos FIIs", candidates.Count);
        foreach (var candidate in candidates.GroupBy(c => c.Ticker, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderByDescending(c => c.PublishedAt).ThenByDescending(c => c.SourceIdentifier ?? string.Empty, StringComparer.Ordinal).First()))
        {
            var asset = assets.Single(a => a.Ticker.Equals(candidate.Ticker, StringComparison.OrdinalIgnoreCase));
            var fingerprint = Document.CreateFingerprint(candidate.Ticker, candidate.PublishedAt, candidate.DocumentType, source.Name, candidate.SourceIdentifier, candidate.SourceUrl);
            if (await db.Documents.AnyAsync(d => d.Fingerprint == fingerprint, ct)) continue;
            var document = new Document(asset.Id, candidate.DocumentType, candidate.PublishedAt, source.Name, candidate.SourceIdentifier, candidate.SourceUrl, fingerprint, candidate.DownloadUrl);
            var job = new ProcessingJob(document.Id, ProcessingJobType.DownloadDocument, DateTimeOffset.UtcNow);
            db.Documents.Add(document); db.ProcessingJobs.Add(job);
            logger.LogInformation("Portfolio import bootstrap scheduled: AssetId={AssetId}, Ticker={Ticker}, JobId={JobId}", asset.Id, asset.Ticker, job.Id);
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Bootstrap LatestOnly concluído para {AssetCount} ativo(s)", assets.Count);
    }

    public async Task<BootstrapRepairResult> EnsurePortfolioDocumentBootstrapAsync(Guid portfolioId, CancellationToken ct)
    {
        var assets = await db.PortfolioPositions.Where(p => p.PortfolioId == portfolioId)
            .Select(p => p.Asset!).Where(a => a.AssetClass == AssetClass.Fii).Distinct().ToListAsync(ct);
        var candidates = new List<Guid>();
        var skipped = 0;
        foreach (var asset in assets)
        {
            var covered = await db.Documents.AnyAsync(d => d.AssetId == asset.Id, ct) ||
                await db.ProcessingJobs.AnyAsync(j => j.Document!.AssetId == asset.Id && (j.Status == ProcessingJobStatus.Queued || j.Status == ProcessingJobStatus.Processing), ct);
            if (covered || string.IsNullOrWhiteSpace(asset.Cnpj)) { skipped++; continue; }
            candidates.Add(asset.Id);
        }
        await DiscoverLatestOnlyAsync(candidates, ct);
        logger.LogInformation("Portfolio bootstrap repair concluído: PortfolioId={PortfolioId}, BootstrapCandidateCount={BootstrapCandidateCount}, BootstrapSkippedExistingCoverageCount={BootstrapSkippedExistingCoverageCount}", portfolioId, candidates.Count, skipped);
        return new BootstrapRepairResult(assets.Count, candidates.Count, skipped);
    }
}

public sealed record BootstrapRepairResult(int FiiCount, int BootstrapCandidateCount, int BootstrapSkippedExistingCoverageCount);
