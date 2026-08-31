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
    private const int MaxAttempts = 3;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(30);
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
                    var usedFallback = false;
                    try
                    {
                        var result = await provider.EvaluateAsync(job.PortfolioId, guidance.RequestedAmount, workItem.Candidates, stoppingToken);
                        if (result.Availability == ContributionIntelligenceAvailability.InsufficientIntelligence)
                        {
                            ApplyFallback(guidance, workItem.Baseline, "A IA não encontrou inteligência suficiente; a distribuição foi calculada pela estratégia e pelas posições atuais da carteira.", "insufficient-intelligence");
                            usedFallback = true;
                        }
                        else if (!ContributionIntelligenceValidator.IsValid(result, guidance.RequestedAmount, workItem.Candidates))
                        {
                            ApplyFallback(guidance, workItem.Baseline, "A resposta da IA não respeitou os limites da carteira; a distribuição segura foi calculada pelas regras anteriores.", "invalid-ai-response");
                            usedFallback = true;
                        }
                        else
                        {
                            guidance.UpdateIntelligence(result.Metadata.Provider, result.Metadata.Model, result.Availability.ToString(), result.OverallReasoning, JsonSerializer.Serialize(result.Metadata));
                            var applied = ContributionIntelligenceApplier.Apply(workItem.Baseline, result, workItem.Positions);
                            var evaluations = result.CandidateEvaluations.ToDictionary(x => x.AssetId);
                            guidance.ReplaceAllocations(applied.Allocations.Select(a => new PersistedContributionGuidanceAllocation(
                                guidance.Id, a.AssetId, a.Amount, a.Reason, evaluations[a.AssetId].Confidence, JsonSerializer.Serialize(a.Evidence))));
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        var (reason, code) = FallbackReason(ex);
                        ApplyFallback(guidance, workItem.Baseline, reason, code);
                        usedFallback = true;
                        logger.LogWarning(ex, "IA indisponível para o job de guidance {JobId}; fallback determinístico aplicado; reason={FallbackReason}", job.Id, code);
                    }

                    job.Complete();
                    metrics.RecordCompleted("ContributionGuidance", stopwatch.Elapsed);
                    await db.SaveChangesAsync(stoppingToken);
                    logger.LogInformation("Job de guidance {JobId} concluído em {DurationMs} ms; backlog={Backlog}; fallback={UsedFallback}", job.Id, stopwatch.Elapsed.TotalMilliseconds, backlog, usedFallback);
                }
                catch (Exception ex)
                {
                    var retry = job.RetryOrFail(ex.Message, MaxAttempts);
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

    private static void ApplyFallback(PersistedContributionGuidance guidance, ContributionGuidance fallback, string reason, string code)
    {
        var applied = ContributionIntelligenceApplier.ApplyFallback(fallback, reason);
        guidance.UpdateIntelligence(
            "deterministic-fallback",
            "portfolio-rules",
            ContributionIntelligenceAvailability.InsufficientIntelligence.ToString(),
            applied.Reasons.FirstOrDefault() ?? reason,
            JsonSerializer.Serialize(new { engineVersion = "R4-3.0", policyVersion = fallback.PortfolioHealthPolicyVersion, fallbackReason = code, generatedAt = DateTimeOffset.UtcNow }));
        guidance.ReplaceAllocations(applied.Allocations.Select(allocation => new PersistedContributionGuidanceAllocation(
            guidance.Id, allocation.AssetId, allocation.Amount, allocation.Reason, 0, JsonSerializer.Serialize(allocation.Evidence))));
    }

    private static (string Reason, string Code) FallbackReason(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } => ("A cota da IA foi excedida; a distribuição foi calculada pela estratégia e pelas posições atuais da carteira.", "ai-quota-exceeded"),
        TimeoutException or TaskCanceledException => ("A IA não respondeu dentro do prazo; a distribuição foi calculada pela estratégia e pelas posições atuais da carteira.", "ai-timeout"),
        _ => ("A IA não respondeu corretamente; a distribuição foi calculada pela estratégia e pelas posições atuais da carteira.", "ai-unavailable")
    };

    private static async Task<ContributionGuidanceJob?> ClaimNextAsync(LodiraDbContext db, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var staleAt = DateTimeOffset.UtcNow.Subtract(Lease);
        var staleJobs = await db.ContributionGuidanceJobs
            .Where(job => job.Status == ContributionGuidanceJobStatus.Processing && job.StartedAt <= staleAt)
            .ToListAsync(ct);
        foreach (var staleJob in staleJobs) staleJob.RequeueExpired();
        if (staleJobs.Count > 0) await db.SaveChangesAsync(ct);
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
