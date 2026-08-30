using Microsoft.EntityFrameworkCore;
using Lodira.Application.Abstractions;
using Lodira.Infrastructure.Persistence;

namespace Lodira.Worker;

/// <summary>Moves only unreferenced, old local files to quarantine; it never deletes them.</summary>
public sealed class StorageMaintenanceWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<StorageMaintenanceWorker> logger;

    public StorageMaintenanceWorker(IServiceScopeFactory scopeFactory, ILogger<StorageMaintenanceWorker> logger)
    {
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LodiraDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
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
}
