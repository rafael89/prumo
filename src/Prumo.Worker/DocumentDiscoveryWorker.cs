using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http;
using Prumo.Application.Abstractions;
using Prumo.Domain.Entities;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Worker;

public sealed class DocumentDiscoveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<DocumentDiscoveryWorker> logger;

    public DocumentDiscoveryWorker(IServiceScopeFactory scopeFactory, IHttpClientFactory httpClientFactory, ILogger<DocumentDiscoveryWorker> logger)
    {
        this.scopeFactory = scopeFactory;
        this.httpClientFactory = httpClientFactory;
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
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var tickers = await dbContext.PortfolioPositions
            .Include(position => position.Asset)
            .Select(position => position.Asset!.Ticker)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (tickers.Count == 0) return;

        var candidates = await source.FindAsync(tickers, cancellationToken);
        var httpClient = httpClientFactory.CreateClient("documents");
        foreach (var candidate in candidates)
        {
            var asset = await dbContext.Assets.SingleOrDefaultAsync(item => item.Ticker == candidate.Ticker.ToUpper(), cancellationToken);
            if (asset is null) continue;

            var fingerprint = Document.CreateFingerprint(candidate.Ticker, candidate.PublishedAt, candidate.DocumentType, source.Name, candidate.SourceIdentifier, candidate.SourceUrl);
            var exists = await dbContext.Documents.AnyAsync(document => document.Fingerprint == fingerprint, cancellationToken);
            if (exists) continue;

            var document = new Document(asset.Id, candidate.DocumentType, candidate.PublishedAt, source.Name, candidate.SourceIdentifier, candidate.SourceUrl, fingerprint);
            dbContext.Documents.Add(document);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(candidate.DownloadUrl))
            {
                try
                {
                    using var response = await httpClient.GetAsync(candidate.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
                    var storedFile = await storage.SaveAsync(document.Id, content, "pdf", cancellationToken);
                    document.MarkDownloaded(storedFile.Path, storedFile.Hash);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException)
                {
                    document.MarkError();
                    await dbContext.SaveChangesAsync(cancellationToken);
                    logger.LogError(exception, "Falha ao baixar documento {DocumentId}", document.Id);
                }
            }
        }
    }
}
