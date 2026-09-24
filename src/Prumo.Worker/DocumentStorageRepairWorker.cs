using Microsoft.EntityFrameworkCore;
using Prumo.Application.Abstractions;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Worker;

/// <summary>Requeues downloads for documents whose persisted local file no longer exists.</summary>
public sealed class DocumentStorageRepairWorker : BackgroundService
{
    private static readonly TimeSpan RepairInterval = TimeSpan.FromHours(1);
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<DocumentStorageRepairWorker> logger;

    public DocumentStorageRepairWorker(IServiceScopeFactory scopeFactory, ILogger<DocumentStorageRepairWorker> logger)
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
                await RepairAsync(stoppingToken);
                await Task.Delay(RepairInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha no reparo de documentos sem arquivo; nova tentativa em {RetryDelay}.", RepairInterval);
                await Task.Delay(RepairInterval, stoppingToken);
            }
        }
    }

    internal async Task RepairAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PrumoDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        var documents = await db.Documents
            .Where(document => document.FilePath != null)
            .ToListAsync(cancellationToken);
        var repaired = 0;

        foreach (var document in documents)
        {
            if (!await db.PortfolioPositions.AnyAsync(position => position.AssetId == document.AssetId && position.Quantity > 0, cancellationToken))
            {
                continue;
            }

            if (await storage.ExistsAsync(document.FilePath!, cancellationToken))
            {
                continue;
            }

            var activeDownload = await db.ProcessingJobs.AnyAsync(job =>
                job.DocumentId == document.Id
                && job.JobType == ProcessingJobType.DownloadDocument
                && (job.Status == ProcessingJobStatus.Queued || job.Status == ProcessingJobStatus.Processing), cancellationToken);
            if (activeDownload) continue;

            var missingPath = document.FilePath;
            document.MarkPendingDownload();
            db.ProcessingJobs.Add(new ProcessingJob(document.Id, ProcessingJobType.DownloadDocument, DateTimeOffset.UtcNow));
            repaired++;
            logger.LogWarning("Arquivo do documento {DocumentId} não foi encontrado em {FilePath}; novo download enfileirado.", document.Id, missingPath);
        }

        if (repaired > 0) await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Reparo de documentos sem arquivo concluído: {RepairCount} download(s) reaberto(s).", repaired);
    }
}
