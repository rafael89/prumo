using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Prumo.Application.Abstractions;
using Prumo.Application.Documents;
using Prumo.Application.PortfolioImports;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Worker;

/// <summary>Deletes only unreferenced, old local files.</summary>
public sealed class StorageMaintenanceWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<StorageMaintenanceWorker> logger;
    private readonly DocumentRetentionOptions retention;

    public StorageMaintenanceWorker(IServiceScopeFactory scopeFactory, ILogger<StorageMaintenanceWorker> logger, IOptions<DocumentRetentionOptions> retention)
    {
        this.scopeFactory = scopeFactory;
        this.logger = logger;
        this.retention = retention.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PrumoDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            await ApplyRetentionAsync(db, storage, stoppingToken);
            var references = (await db.Documents.AsNoTracking().Where(document => document.FilePath != null).Select(document => document.FilePath!).ToListAsync(stoppingToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var moved = await storage.DeleteUnreferencedAsync(references, TimeSpan.FromHours(24), stoppingToken);
            logger.LogInformation("Manutenção do storage concluída: {DeletedCount} arquivo(s) antigo(s) sem referência removido(s).", moved.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha na manutenção do storage; nenhum arquivo foi apagado.");
        }
    }

    private async Task ApplyRetentionAsync(PrumoDbContext db, IFileStorage storage, CancellationToken cancellationToken)
    {
        if (retention.KeepPdfAfterAnalysis || retention.RetainPdfDays is not > 0) return;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retention.RetainPdfDays.Value);
        var documents = await db.Documents
            .Include(document => document.Asset)
            .Where(document => document.ProcessingStatus == DocumentProcessingStatus.Processed
                && document.FilePath != null
                && document.ProcessedAt != null
                && document.ProcessedAt <= cutoff)
            .ToListAsync(cancellationToken);
        var expired = documents.Where(document => !DocumentClassificationPolicy.MayReportDistribution(document.Asset!.AssetClass,
            document.DocumentType, document.SourceCategory, document.SourceType, document.SourceSpecies)).ToList();
        foreach (var document in expired)
        {
            await storage.DeleteAsync(document.FilePath!, cancellationToken);
            document.ReleaseStoredFile();
        }
        if (expired.Count > 0) await db.SaveChangesAsync(cancellationToken);
        if (expired.Count > 0) logger.LogInformation("Política de retenção removeu {RemovedCount} PDF(s) já processado(s).", expired.Count);
    }
}
