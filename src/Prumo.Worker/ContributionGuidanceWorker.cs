using System.Text.Json;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Prumo.Application.Contributions;
using Prumo.Domain.Entities;
using Prumo.Infrastructure.Persistence;
using Prumo.Application.Abstractions;
using Prumo.Infrastructure.MarketData;
using Prumo.Domain.Enums;

namespace Prumo.Worker;

public sealed class ContributionGuidanceWorker(IServiceScopeFactory scopes, ILogger<ContributionGuidanceWorker> logger, WorkerMetrics metrics) : BackgroundService
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(30);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid? claimedJobId = null;
            try
            {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PrumoDbContext>(); var job = await ClaimNextAsync(db, stoppingToken);
                if (job is null) { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); continue; }
                claimedJobId = job.Id;
                var stopwatch = Stopwatch.StartNew();
                var backlog = await db.ContributionGuidanceJobs.CountAsync(x => x.Status == ContributionGuidanceJobStatus.Queued, stoppingToken);
                metrics.SetBacklog("ContributionGuidance", backlog);
                try
                {
                    var guidance = await db.ContributionGuidances.Include(x => x.Allocations).SingleAsync(x => x.Id == job.ContributionGuidanceId, stoppingToken);
                    var workItem = JsonSerializer.Deserialize<ContributionGuidanceWorkItem>(job.CandidatesJson)
                        ?? throw new InvalidOperationException("Contexto da orientação de aporte inválido.");
                    workItem = await AddExternalCandidatesAsync(scope.ServiceProvider, workItem, stoppingToken);
                    var deterministicDecision = new PortfolioDecisionService().DecideDeterministically(guidance.RequestedAmount, workItem.Candidates);
                    var usedFallback = false;
                    var deterministicAllocations = deterministicDecision.RecommendedScenario?.Allocations.Select(allocation =>
                    {
                        var candidate = workItem.Candidates.Single(x => x.CandidateKey == allocation.Key);
                        return new ContributionAllocation(candidate.Identity.AssetId, candidate.Ticker, candidate.AssetClass, allocation.Amount, deterministicDecision.DecisionEvidence?.DecisiveFactor ?? "PrimaryStructuralImprovement", Array.Empty<string>(), candidate.Identity);
                    }).ToArray() ?? Array.Empty<ContributionAllocation>();
                    guidance.UpdateIntelligence("deterministic", "portfolio-rules", "Deterministic", string.Join("\n", deterministicDecision.Reasons), JsonSerializer.Serialize(new { engineVersion = "R16.6.2", deterministicDecision.DecisionEvidence }));
                    guidance.ReplaceAllocations(deterministicAllocations.Select(a => PersistedAllocation(guidance.Id, new CandidateAllocation(a.Identity, a.Amount), workItem.Candidates.Single(x => x.CandidateKey == a.Identity.Key), 1)).ToArray());
                    AppendProjectedPortfolioReason(guidance, workItem);
                    var publication = ContributionGuidancePublication.From(guidance);
                    var published = await PublishAsync(job.Id, publication, stoppingToken);
                    if (!published)
                    {
                        logger.LogWarning("Resultado do job de guidance {JobId} descartado porque o job não está mais em processamento", job.Id);
                        continue;
                    }
                    metrics.RecordCompleted("ContributionGuidance", stopwatch.Elapsed);
                    logger.LogInformation("Job de guidance {JobId} concluído em {DurationMs} ms; backlog={Backlog}; fallback={UsedFallback}", job.Id, stopwatch.Elapsed.TotalMilliseconds, backlog, usedFallback);
                }
                catch (Exception ex)
                {
                    metrics.RecordFailed("ContributionGuidance", stopwatch.Elapsed);
                    await RecoverClaimedJobAsync(job.Id, ex.Message, stoppingToken);
                    logger.LogError(ex, "Falha no job de guidance {JobId} após {DurationMs} ms; backlog={Backlog}; nenhuma alocação foi publicada", job.Id, stopwatch.Elapsed.TotalMilliseconds, backlog);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (claimedJobId.HasValue)
                {
                    await RecoverClaimedJobAsync(claimedJobId.Value, ex.Message, stoppingToken);
                }
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
            JsonSerializer.Serialize(new { engineVersion = "R4-9.0", policyVersion = fallback.PortfolioHealthPolicyVersion, assessmentVersions = fallback.AssessmentVersions, fallbackReason = code, generatedAt = DateTimeOffset.UtcNow, classAllocations = applied.ClassAllocations }));
        guidance.ReplaceAllocations(applied.Allocations.Select(allocation => new PersistedContributionGuidanceAllocation(
            guidance.Id, allocation.AssetId!.Value, allocation.Amount, allocation.Reason, 0, JsonSerializer.Serialize(allocation.Evidence))));
    }

    private static (string Reason, string Code) FallbackReason(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } => ("A cota da IA foi excedida; a distribuição foi calculada pela estratégia e pelas posições atuais da carteira.", "ai-quota-exceeded"),
        TimeoutException or TaskCanceledException => ("A IA não respondeu dentro do prazo; a distribuição foi calculada pela estratégia e pelas posições atuais da carteira.", "ai-timeout"),
        _ => ("A IA não respondeu corretamente; a distribuição foi calculada pela estratégia e pelas posições atuais da carteira.", "ai-unavailable")
    };

    private static void AppendProjectedPortfolioReason(PersistedContributionGuidance guidance, ContributionGuidanceWorkItem workItem)
    {
        var candidatesByKey = workItem.Candidates.ToDictionary(x => x.CandidateKey);
        var classCandidates = workItem.Candidates.GroupBy(x => x.AssetClass).ToArray();
        var currentTotal = workItem.Candidates.Sum(x => x.CurrentValue);
        var allocatedTotal = guidance.Allocations.Sum(x => x.Amount);
        if (classCandidates.Length == 0 || currentTotal + allocatedTotal <= 0) return;

        var allocatedByClass = guidance.Allocations
            .Where(x => x.AssetId is not null && candidatesByKey.ContainsKey(CandidateIdentity.Known(x.AssetId.Value).Key))
            .GroupBy(x => candidatesByKey[CandidateIdentity.Known(x.AssetId!.Value).Key].AssetClass)
            .ToDictionary(x => x.Key, x => x.Sum(item => item.Amount));
        var before = classCandidates.Sum(group => Math.Abs(group.First().CurrentClassWeight - group.First().TargetClassWeight));
        var after = classCandidates.Sum(group =>
        {
            var first = group.First();
            var projectedWeight = (group.Sum(x => x.CurrentValue) + allocatedByClass.GetValueOrDefault(group.Key))
                / (currentTotal + allocatedTotal) * 100m;
            return Math.Abs(projectedWeight - first.TargetClassWeight);
        });
        var direction = after < before ? "reduziu" : after > before ? "aumentou" : "manteve";
        var projection = $"Simulação pós-aporte: o desvio agregado das classes {direction} de {before:0.##} para {after:0.##} p.p. considerando {allocatedTotal:0.##} alocados.";
        guidance.UpdateIntelligence(guidance.Provider, guidance.Model, guidance.IntelligenceAvailability,
            string.IsNullOrWhiteSpace(guidance.Reasoning) ? projection : $"{guidance.Reasoning}\n{projection}", guidance.MetadataJson);
    }

    private static PersistedContributionGuidanceAllocation PersistedAllocation(Guid guidanceId, CandidateAllocation allocation, ContributionCandidate candidate, decimal confidence)
    {
        var identity = allocation.Identity;
        if (identity.AssetId is Guid assetId) return new(guidanceId, assetId, allocation.Amount, candidate.Ticker, confidence, "[]");
        var external = identity.External!;
        return new(guidanceId, identity.Key.Value, external.Provider, candidate.Ticker, candidate.ExternalName ?? candidate.Ticker, candidate.AssetClass, candidate.AssetType, external.Exchange, candidate.TradingCurrency, candidate.Sector, candidate.ExternalSourceReference, allocation.Amount, candidate.Ticker, confidence, "[]");
    }

    private static async Task<ContributionGuidanceWorkItem> AddExternalCandidatesAsync(IServiceProvider services, ContributionGuidanceWorkItem item, CancellationToken ct)
    {
        var target = item.Baseline.ClassAllocations.OrderByDescending(x => x.Amount).FirstOrDefault()?.AssetClass;
        if (target is null) return item;
        try
        {
            var discovered = await services.GetRequiredService<ExternalAssetDiscoveryService>().DiscoverAsync(target.Value, item.Candidates.Select(x => x.Ticker), ct);
            var deviation = item.Candidates.FirstOrDefault(x => x.AssetClass == target.Value);
            var external = discovered.Select(x => new ContributionCandidate(CandidateIdentity.FromExternal(x.Identity), x.Ticker, target.Value, 0, 0, deviation?.CurrentClassWeight ?? 0, deviation?.TargetClassWeight ?? 100, item.Baseline.ClassAllocations.FirstOrDefault(a => a.AssetClass == target.Value)?.Amount ?? 0, false, Array.Empty<ContributionAssessmentContext>(), x.AssetType, x.Sector, HasPosition: false, AllocationNeedScore: 100, UnderweightScore: 100) { ExternalName = x.Name, ExternalSourceReference = x.SourceReference }).ToArray();
            return item with { Candidates = item.Candidates.Concat(external).ToArray() };
        }
        catch { return item; }
    }

    private static async Task<ContributionGuidanceJob?> ClaimNextAsync(PrumoDbContext db, CancellationToken ct)
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

    private async Task RecoverClaimedJobAsync(Guid jobId, string error, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PrumoDbContext>();
            var job = await db.ContributionGuidanceJobs.SingleOrDefaultAsync(x => x.Id == jobId, ct);
            if (job?.Status != ContributionGuidanceJobStatus.Processing) return;
            job.RetryOrFail(error, MaxAttempts);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception recoveryException)
        {
            logger.LogError(recoveryException, "Falha ao recuperar o job de guidance {JobId} após erro inesperado", jobId);
        }
    }

    private async Task<bool> PublishAsync(Guid jobId, ContributionGuidancePublication publication, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PrumoDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var job = await db.ContributionGuidanceJobs
            .FromSqlInterpolated($"SELECT * FROM \"ContributionGuidanceJobs\" WHERE \"Id\" = {jobId} FOR UPDATE")
            .SingleAsync(ct);
        if (job.Status != ContributionGuidanceJobStatus.Processing)
        {
            await transaction.CommitAsync(ct);
            return false;
        }

        var guidance = await db.ContributionGuidances
            .SingleAsync(x => x.Id == job.ContributionGuidanceId, ct);
        guidance.UpdateIntelligence(publication.Provider, publication.Model, publication.Availability, publication.Reasoning, publication.MetadataJson);

        // The allocations carry application-generated GUIDs. When they are only attached
        // through an already tracked navigation, EF can infer that they already exist and
        // emit UPDATEs instead of INSERTs. Persist the replacement explicitly so publication
        // remains atomic and retries do not depend on the change tracker's graph inference.
        await db.ContributionGuidanceAllocations
            .Where(x => x.ContributionGuidanceId == guidance.Id)
            .ExecuteDeleteAsync(ct);
        db.ContributionGuidanceAllocations.AddRange(publication.Allocations.Select(x =>
            x.Origin == "KnownCatalog"
                ? new PersistedContributionGuidanceAllocation(guidance.Id, x.AssetId!.Value, x.Amount, x.Reason, x.Confidence, x.EvidenceJson)
                : new PersistedContributionGuidanceAllocation(guidance.Id, x.CandidateKey!, x.Provider!, x.Ticker!, x.Name!, x.AssetClass!.Value, x.AssetType!.Value, x.Exchange, x.Currency, x.Sector, x.SourceReference, x.Amount, x.Reason, x.Confidence, x.EvidenceJson)));
        job.Complete();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            var entries = string.Join(", ", exception.Entries.Select(x => $"{x.Metadata.ClrType.Name}:{x.State}"));
            logger.LogError(exception,
                "Conflito ao publicar o job de guidance {JobId}; entidades={ConcurrencyEntries}",
                jobId,
                entries);
            throw;
        }
        await transaction.CommitAsync(ct);
        return true;
    }

    private sealed record ContributionGuidancePublication(
        string Provider,
        string Model,
        string Availability,
        string Reasoning,
        string MetadataJson,
        IReadOnlyList<ContributionGuidanceAllocationPublication> Allocations)
    {
        public static ContributionGuidancePublication From(PersistedContributionGuidance guidance) => new(
            guidance.Provider,
            guidance.Model,
            guidance.IntelligenceAvailability,
            guidance.Reasoning,
            guidance.MetadataJson,
            guidance.Allocations.Select(x => new ContributionGuidanceAllocationPublication(
                x.Origin, x.AssetId, x.CandidateKey, x.Provider, x.Ticker, x.Name, x.AssetClass, x.AssetType, x.Exchange, x.Currency, x.Sector, x.SourceReference, x.Amount, x.Reason, x.Confidence, x.EvidenceJson)).ToArray());
    }

    private sealed record ContributionGuidanceAllocationPublication(
        string Origin,
        Guid? AssetId,
        string? CandidateKey,
        string? Provider,
        string? Ticker,
        string? Name,
        AssetClass? AssetClass,
        AssetType? AssetType,
        string? Exchange,
        string? Currency,
        string? Sector,
        string? SourceReference,
        decimal Amount,
        string Reason,
        decimal Confidence,
        string EvidenceJson);
}

