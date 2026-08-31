using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Lodira.Application.Abstractions;
using Lodira.Application.PortfolioImports;
using Lodira.Domain.Enums;
using Lodira.Infrastructure.Persistence;

namespace Lodira.Worker;

/// <summary>Moves only unreferenced, old local files to quarantine; it never deletes them.</summary>
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
            var db = scope.ServiceProvider.GetRequiredService<LodiraDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            await ApplyRetentionAsync(db, storage, stoppingToken);
            var references = (await db.Documents.AsNoTracking().Where(document => document.FilePath != null).Select(document => document.FilePath!).ToListAsync(stoppingToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var moved = await storage.MoveUnreferencedAsync(references, TimeSpan.FromHours(24), stoppingToken);
            logger.LogInformation("Manutenção do storage concluída: {MovedCount} arquivo(s) antigo(s) sem referência movido(s) para quarantine.", moved.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha na manutenção do storage; nenhum arquivo foi apagado.");
        }
    }

    private async Task ApplyRetentionAsync(LodiraDbContext db, IFileStorage storage, CancellationToken cancellationToken)
    {
        if (retention.KeepPdfAfterAnalysis || retention.RetainPdfDays is not > 0) return;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retention.RetainPdfDays.Value);
        var documents = await db.Documents
            .Where(document => document.ProcessingStatus == DocumentProcessingStatus.Processed
                && document.FilePath != null
                && document.ProcessedAt != null
                && document.ProcessedAt <= cutoff)
            .ToListAsync(cancellationToken);
        foreach (var document in documents)
        {
            await storage.DeleteAsync(document.FilePath!, cancellationToken);
            document.ReleaseStoredFile();
        }
        if (documents.Count > 0) await db.SaveChangesAsync(cancellationToken);
        if (documents.Count > 0) logger.LogInformation("Política de retenção removeu {RemovedCount} PDF(s) já processado(s).", documents.Count);
    }
}
