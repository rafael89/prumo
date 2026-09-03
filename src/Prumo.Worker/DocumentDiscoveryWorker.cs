using Microsoft.EntityFrameworkCore;
using Prumo.Application.Abstractions;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.PortfolioImports;

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
            try
            {
                await DiscoverAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Erro inesperado no ciclo de descoberta; nova tentativa em {RetryDelay}.", TimeSpan.FromMinutes(1));
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task DiscoverAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Ciclo de descoberta de documentos iniciado");

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PrumoDbContext>();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        if (!await TryAcquireDiscoveryLockAsync(dbContext, cancellationToken))
        {
            await dbContext.Database.CloseConnectionAsync();
            logger.LogInformation("Ciclo de descoberta ignorado: outra instância já está executando a descoberta.");
            return;
        }
        try
        {
        var source = scope.ServiceProvider.GetRequiredService<IDocumentSource>();
        var assets = await dbContext.PortfolioPositions
            .Include(position => position.Asset)
            .Where(position => position.Asset != null)
            .Select(position => position.Asset!)
            .Distinct()
            .ToListAsync(cancellationToken);

        logger.LogInformation("Ciclo de descoberta encontrou {TickerCount} ticker(s): {Tickers}", assets.Count, string.Join(", ", assets.Select(asset => asset.Ticker)));
        if (assets.Count == 0)
        {
            logger.LogInformation("Ciclo de descoberta encerrado: não há posições cadastradas");
            return;
        }

        IReadOnlyList<DocumentCandidate> candidates;
        try
        {
            candidates = await source.FindAsync(assets.Select(asset => new DocumentSourceAsset(asset.Ticker, asset.Cnpj, asset.AssetClass)).ToArray(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Erro da fonte {SourceName} durante a descoberta dos tickers {Tickers}", source.Name, string.Join(", ", assets.Select(asset => asset.Ticker)));
            return;
        }

        var assetByTicker = assets.ToDictionary(asset => asset.Ticker, StringComparer.OrdinalIgnoreCase);
        var reclassifiedDocuments = 0;
        foreach (var candidate in candidates.Where(candidate => assetByTicker.ContainsKey(candidate.Ticker)))
        {
            var asset = assetByTicker[candidate.Ticker];
            var existing = await dbContext.Documents.FirstOrDefaultAsync(document =>
                document.AssetId == asset.Id && document.SourceName == source.Name &&
                ((!string.IsNullOrWhiteSpace(candidate.SourceIdentifier) && document.SourceIdentifier == candidate.SourceIdentifier) || document.SourceUrl == candidate.SourceUrl), cancellationToken);
            if (existing is not null)
            {
                existing.UpdateSourceMetadata(candidate.SourceCategory, candidate.SourceType, candidate.SourceSpecies, candidate.DocumentType);
                reclassifiedDocuments++;
            }
        }
        if (reclassifiedDocuments > 0) await dbContext.SaveChangesAsync(cancellationToken);

        var incrementalCandidates = LatestDocumentSelector.SelectIncremental(candidates).ToList();
        logger.LogInformation("Fonte {SourceName} retornou {DocumentCount} candidato(s); todos os candidatos elegíveis serão verificados contra a deduplicação", source.Name, candidates.Count);
        var newDocuments = 0;
        var duplicatedDocuments = 0;
        foreach (var candidate in incrementalCandidates)
        {
            logger.LogInformation("Processando documento candidato do ticker {Ticker}: tipo={DocumentType}, identificador={SourceIdentifier}, url={SourceUrl}", candidate.Ticker, candidate.DocumentType, candidate.SourceIdentifier, candidate.SourceUrl);
            var asset = await dbContext.Assets.SingleOrDefaultAsync(item => item.Ticker == candidate.Ticker.ToUpper(), cancellationToken);
            if (asset is null)
            {
                logger.LogWarning("Documento candidato ignorado: ticker {Ticker} não está cadastrado em Assets", candidate.Ticker);
                continue;
            }

            var fingerprint = Document.CreateFingerprint(candidate.Ticker, candidate.PublishedAt, candidate.DocumentType, source.Name, candidate.SourceIdentifier, candidate.SourceUrl);
            var exists = await dbContext.Documents.AnyAsync(document =>
                document.Fingerprint == fingerprint ||
                (document.AssetId == asset.Id && document.SourceName == source.Name &&
                 ((!string.IsNullOrWhiteSpace(candidate.SourceIdentifier) && document.SourceIdentifier == candidate.SourceIdentifier) || document.SourceUrl == candidate.SourceUrl)), cancellationToken);
            if (exists)
            {
                duplicatedDocuments++;
                logger.LogInformation("Documento candidato ignorado por deduplicação: ticker={Ticker}, fingerprint={Fingerprint}", candidate.Ticker, fingerprint);
                continue;
            }

            var document = new Document(asset.Id, candidate.DocumentType, candidate.PublishedAt, source.Name, candidate.SourceIdentifier, candidate.SourceUrl, fingerprint, candidate.DownloadUrl, candidate.SourceCategory, candidate.SourceType, candidate.SourceSpecies);
            dbContext.Documents.Add(document);
            dbContext.ProcessingJobs.Add(new ProcessingJob(document.Id, ProcessingJobType.DownloadDocument, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync(cancellationToken);

            newDocuments++;
            logger.LogInformation("Documento {DocumentId} descoberto e job de download enfileirado", document.Id);
        }

        logger.LogInformation("Ciclo de descoberta encerrado: {NewDocumentCount} documento(s) novo(s), {DuplicatedDocumentCount} ignorado(s) por deduplicação", newDocuments, duplicatedDocuments);
        }
        finally
        {
            try { await ReleaseDiscoveryLockAsync(dbContext, CancellationToken.None); }
            finally { await dbContext.Database.CloseConnectionAsync(); }
        }
    }

    private static async Task<bool> TryAcquireDiscoveryLockAsync(PrumoDbContext dbContext, CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(hashtext('prumo:document-discovery'))";
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task ReleaseDiscoveryLockAsync(PrumoDbContext dbContext, CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_advisory_unlock(hashtext('prumo:document-discovery'))";
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
