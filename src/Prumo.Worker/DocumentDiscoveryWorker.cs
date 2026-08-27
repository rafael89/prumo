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
        logger.LogInformation("Ciclo de descoberta de documentos iniciado");

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PrumoDbContext>();
        var source = scope.ServiceProvider.GetRequiredService<IDocumentSource>();
        var tickers = await dbContext.PortfolioPositions
            .Include(position => position.Asset)
            .Select(position => position.Asset!.Ticker)
            .Distinct()
            .ToListAsync(cancellationToken);

        logger.LogInformation("Ciclo de descoberta encontrou {TickerCount} ticker(s): {Tickers}", tickers.Count, string.Join(", ", tickers));
        if (tickers.Count == 0)
        {
            logger.LogInformation("Ciclo de descoberta encerrado: não há posições cadastradas");
            return;
        }

        IReadOnlyList<DocumentCandidate> candidates;
        try
        {
            candidates = await source.FindAsync(tickers, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Erro da fonte {SourceName} durante a descoberta dos tickers {Tickers}", source.Name, string.Join(", ", tickers));
            return;
        }

        logger.LogInformation("Fonte {SourceName} retornou {DocumentCount} documento(s) candidato(s)", source.Name, candidates.Count);
        var newDocuments = 0;
        var duplicatedDocuments = 0;
        foreach (var candidate in candidates)
        {
            logger.LogInformation("Processando documento candidato do ticker {Ticker}: tipo={DocumentType}, identificador={SourceIdentifier}, url={SourceUrl}", candidate.Ticker, candidate.DocumentType, candidate.SourceIdentifier, candidate.SourceUrl);
            var asset = await dbContext.Assets.SingleOrDefaultAsync(item => item.Ticker == candidate.Ticker.ToUpper(), cancellationToken);
            if (asset is null)
            {
                logger.LogWarning("Documento candidato ignorado: ticker {Ticker} não está cadastrado em Assets", candidate.Ticker);
                continue;
            }

            var fingerprint = Document.CreateFingerprint(candidate.Ticker, candidate.PublishedAt, candidate.DocumentType, source.Name, candidate.SourceIdentifier, candidate.SourceUrl);
            var exists = await dbContext.Documents.AnyAsync(document => document.Fingerprint == fingerprint, cancellationToken);
            if (exists)
            {
                duplicatedDocuments++;
                logger.LogInformation("Documento candidato ignorado por deduplicação: ticker={Ticker}, fingerprint={Fingerprint}", candidate.Ticker, fingerprint);
                continue;
            }

            var document = new Document(asset.Id, candidate.DocumentType, candidate.PublishedAt, source.Name, candidate.SourceIdentifier, candidate.SourceUrl, fingerprint, candidate.DownloadUrl);
            dbContext.Documents.Add(document);
            dbContext.ProcessingJobs.Add(new ProcessingJob(document.Id, ProcessingJobType.DownloadDocument, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync(cancellationToken);

            newDocuments++;
            logger.LogInformation("Documento {DocumentId} descoberto e job de download enfileirado", document.Id);
        }

        logger.LogInformation("Ciclo de descoberta encerrado: {NewDocumentCount} documento(s) novo(s), {DuplicatedDocumentCount} ignorado(s) por deduplicação", newDocuments, duplicatedDocuments);
    }
}
