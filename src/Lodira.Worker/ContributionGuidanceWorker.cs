using System.Text.Json;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Lodira.Application.Contributions;
using Lodira.Domain.Entities;
using Lodira.Infrastructure.Persistence;
using Lodira.Domain.Enums;

namespace Lodira.Worker;

public sealed class ContributionGuidanceWorker(IServiceScopeFactory scopes, ILogger<ContributionGuidanceWorker> logger, WorkerMetrics metrics) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LodiraDbContext>(); var job = await ClaimNextAsync(db, stoppingToken);
                if (job is null) { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); continue; }
                var stopwatch = Stopwatch.StartNew();
                var backlog = await db.ContributionGuidanceJobs.CountAsync(x => x.Status == ContributionGuidanceJobStatus.Queued, stoppingToken);
                metrics.SetBacklog("ContributionGuidance", backlog);
                try
                {
                    var guidance = await db.ContributionGuidances.Include(x => x.Allocations).SingleAsync(x => x.Id == job.ContributionGuidanceId, stoppingToken);
                    var workItem = JsonSerializer.Deserialize<ContributionGuidanceWorkItem>(job.CandidatesJson)
                        ?? throw new InvalidOperationException("Contexto da orientação de aporte inválido.");
                    var provider = scope.ServiceProvider.GetRequiredService<IContributionIntelligenceProvider>();
                    var result = await provider.EvaluateAsync(job.PortfolioId, guidance.RequestedAmount, workItem.Candidates, stoppingToken);
                    if (!ContributionIntelligenceValidator.IsValid(result, guidance.RequestedAmount, workItem.Candidates))
                        throw new InvalidOperationException("A IA retornou uma distribuição que viola os limites da carteira.");

                    guidance.UpdateIntelligence(result.Metadata.Provider, result.Metadata.Model, result.Availability.ToString(), result.OverallReasoning, JsonSerializer.Serialize(result.Metadata));
                    var applied = ContributionIntelligenceApplier.Apply(workItem.Baseline, result, workItem.Positions);
                    var evaluations = result.CandidateEvaluations.ToDictionary(x => x.AssetId);
                    guidance.ReplaceAllocations(applied.Allocations.Select(a => new PersistedContributionGuidanceAllocation(
                        guidance.Id, a.AssetId, a.Amount, a.Reason, evaluations[a.AssetId].Confidence, JsonSerializer.Serialize(a.Evidence))));
                    job.Complete();
                    metrics.RecordCompleted("ContributionGuidance", stopwatch.Elapsed);
                    await db.SaveChangesAsync(stoppingToken);
                    logger.LogInformation("Job de guidance {JobId} concluído em {DurationMs} ms; backlog={Backlog}", job.Id, stopwatch.Elapsed.TotalMilliseconds, backlog);
                }
                catch (Exception ex)
                {
                    job.Fail(ex.Message);
                    metrics.RecordFailed("ContributionGuidance", stopwatch.Elapsed);
                    await db.SaveChangesAsync(stoppingToken);
                    logger.LogError(ex, "Falha no job de guidance {JobId} após {DurationMs} ms; backlog={Backlog}; nenhuma alocação foi publicada", job.Id, stopwatch.Elapsed.TotalMilliseconds, backlog);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro inesperado no polling de jobs de guidance; nova tentativa em {RetryDelay}", TimeSpan.FromSeconds(2));
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private static async Task<ContributionGuidanceJob?> ClaimNextAsync(LodiraDbContext db, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var queued = (int)ContributionGuidanceJobStatus.Queued;
        var job = await db.ContributionGuidanceJobs
            .FromSqlInterpolated($"""
                SELECT *
                FROM "ContributionGuidanceJobs"
                WHERE "Status" = {queued}
                ORDER BY "CreatedAt"
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """)
            .SingleOrDefaultAsync(ct);

        if (job is null)
        {
            await transaction.CommitAsync(ct);
            return null;
        }

        job.Start();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return job;
    }
}
