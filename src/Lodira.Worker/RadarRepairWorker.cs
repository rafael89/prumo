using Microsoft.EntityFrameworkCore;
using Lodira.Domain.Enums;
using Lodira.Domain.Entities;
using Lodira.Infrastructure.Persistence;

namespace Lodira.Worker;

/// <summary>Reopens only already analyzed relevant documents that have no persisted event.</summary>
public sealed class RadarRepairWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<RadarRepairWorker> logger;

    public RadarRepairWorker(IServiceScopeFactory scopeFactory, ILogger<RadarRepairWorker> logger)
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
            var candidates = await db.DocumentAnalyses
                .Where(analysis => analysis.Importance == AnalysisImportance.Critical || analysis.Importance == AnalysisImportance.Relevant)
                .Where(analysis => db.InvestmentEvents.Any(eventItem => eventItem.DocumentAnalysisId == analysis.Id))
                .Select(analysis => analysis.DocumentId)
                .ToListAsync(stoppingToken);
            var existing = await db.ProcessingJobs
                .Where(job => job.JobType == ProcessingJobType.AnalyzeDocument && candidates.Contains(job.DocumentId) && (job.Status == ProcessingJobStatus.Queued || job.Status == ProcessingJobStatus.Processing))
                .Select(job => job.DocumentId)
                .ToHashSetAsync(stoppingToken);
            var now = DateTimeOffset.UtcNow;
            foreach (var documentId in candidates.Where(documentId => !existing.Contains(documentId)))
            {
                db.ProcessingJobs.Add(new ProcessingJob(documentId, ProcessingJobType.AnalyzeDocument, now));
            }
            await db.SaveChangesAsync(stoppingToken);
            logger.LogInformation("Reparo do Radar concluído: {RepairCount} análise(s) relevante(s) reaberta(s) sem nova chamada à IA.", candidates.Count(documentId => !existing.Contains(documentId)));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha no reparo inicial do Radar.");
        }
    }
}
