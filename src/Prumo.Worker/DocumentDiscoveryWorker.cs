using Microsoft.EntityFrameworkCore;
using Prumo.Application.Abstractions;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Worker;

public sealed class DocumentDiscoveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<DocumentDiscoveryWorker> logger;

    public DocumentDiscoveryWorker(IServiceScopeFactory scopeFactory, ILogger<DocumentDiscoveryWorker> logger)
    {
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await DiscoverAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    private async Task DiscoverAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PrumoDbContext>();
        var source = scope.ServiceProvider.GetRequiredService<IDocumentSource>();
        var tickers = await dbContext.PortfolioPositions
            .Include(position => position.Asset)
            .Select(position => position.Asset!.Ticker)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (tickers.Count == 0) return;

        var candidates = await source.FindAsync(tickers, cancellationToken);
        foreach (var candidate in candidates)
        {
            var asset = await dbContext.Assets.SingleOrDefaultAsync(item => item.Ticker == candidate.Ticker.ToUpper(), cancellationToken);
            if (asset is null) continue;

            var fingerprint = Document.CreateFingerprint(candidate.Ticker, candidate.PublishedAt, candidate.DocumentType, source.Name, candidate.SourceIdentifier, candidate.SourceUrl);
            var exists = await dbContext.Documents.AnyAsync(document => document.Fingerprint == fingerprint, cancellationToken);
            if (exists) continue;

            var document = new Document(asset.Id, candidate.DocumentType, candidate.PublishedAt, source.Name, candidate.SourceIdentifier, candidate.SourceUrl, fingerprint, candidate.DownloadUrl);
            dbContext.Documents.Add(document);
            dbContext.ProcessingJobs.Add(new ProcessingJob(document.Id, ProcessingJobType.DownloadDocument, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Documento {DocumentId} descoberto e job de download enfileirado", document.Id);
        }
    }
}
