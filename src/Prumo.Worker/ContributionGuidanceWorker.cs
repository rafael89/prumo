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
                if (job is null) { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); continue; }
                claimedJobId = job.Id;
                var stopwatch = Stopwatch.StartNew();
                var backlog = await db.ContributionGuidanceJobs.CountAsync(x => x.Status == ContributionGuidanceJobStatus.Queued, stoppingToken);
                metrics.SetBacklog("ContributionGuidance", backlog);
                try
                {
                    var guidance = await db.ContributionGuidances.Include(x => x.Allocations).SingleAsync(x => x.Id == job.ContributionGuidanceId, stoppingToken);
                    var workItem = JsonSerializer.Deserialize<ContributionGuidanceWorkItem>(job.CandidatesJson)
                        ?? throw new InvalidOperationException("Contexto da orientação de aporte inválido.");
                    var investorPolicy = InvestorPolicy.Current;
                    var deterministicDecision = new PortfolioDecisionService().DecideDeterministically(guidance.RequestedAmount, workItem.Candidates, investorPolicy);
                    var usedFallback = false;
                    var deterministicOptions = BuildScenarioOptions(deterministicDecision, workItem.Baseline, workItem.Candidates);
                    var eligibleCandidates = workItem.Candidates.Where(candidate => !candidate.HasBlock && candidate.ClassBudget > 0).ToArray();
                    if (eligibleCandidates.Length == 0)
                    {
                        usedFallback = true;
                        ApplyFallback(
                            guidance,
                            workItem.Baseline,
                            deterministicDecision.Reasons.FirstOrDefault() ?? "Não há candidato elegível dentro dos limites atuais.",
                            "no-eligible-candidates");
                    }
                    else
                    {
                        try
                        {
                            var (proposal, allocations, retryCount) = await ProposeValidatedAllocationAsync(
                                scope.ServiceProvider.GetRequiredService<IContributionIntelligenceProvider>(), job.PortfolioId,
                                guidance.RequestedAmount, workItem.Candidates, deterministicOptions, investorPolicy, stoppingToken);
                            var allocationMode = AllocationMode(allocations, deterministicOptions);
                            metrics.RecordAiOutcome(allocationMode);
                            guidance.UpdateIntelligence(proposal.Metadata.Provider, proposal.Metadata.Model, proposal.Availability.ToString(), proposal.Reason, JsonSerializer.Serialize(new
                            {
                                engineVersion = proposal.Metadata.EngineVersion,
                                policyVersion = proposal.Metadata.PolicyVersion,
                                promptVersion = proposal.Metadata.PromptVersion,
                                generatedAt = proposal.Metadata.GeneratedAt,
                                deterministicBaselineScenarioKey = deterministicOptions.FirstOrDefault()?.ScenarioKey,
                                usedFallback = false,
                                selectionMode = "ai-constrained-allocation",
                                allocationMode,
                                retryCount,
                                investorPolicyId = investorPolicy.PolicyId,
                                investorPolicyVersion = investorPolicy.PolicyVersion,
                                decisionType = deterministicDecision.DecisionType.ToString(),
                                rebalanceMode = deterministicDecision.RebalanceMode.ToString(),
                                primaryNeed = deterministicDecision.PrimaryNeed,
                                structuralNeeds = deterministicDecision.StructuralNeeds,
                                decisionEvidence = deterministicDecision.DecisionEvidence,
                                assessmentVersions = workItem.Baseline.AssessmentVersions,
                                evidenceCount = workItem.Baseline.Evidence.Count,
                                primaryFactors = proposal.PrimaryFactors,
                                confidence = proposal.Confidence,
                                proposalDecisionType = proposal.DecisionType
                            }));
                            guidance.ReplaceAllocations(ToPersistedAllocations(guidance.Id, allocations, workItem.Candidates, proposal.Confidence));
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            usedFallback = true;
                            ApplyFallback(guidance, workItem.Baseline, FallbackReason(ex).Reason, FallbackReason(ex).Code);
                            logger.LogWarning(ex, "Resposta da IA não utilizável para o job de guidance {JobId}; fallback determinístico aplicado", job.Id);
                        }
                    }
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
                logger.LogError(ex, "Erro inesperado no polling de jobs de guidance; nova tentativa em {RetryDelay}", TimeSpan.FromSeconds(10));
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
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
            JsonSerializer.Serialize(new { engineVersion = "R17.8", policyVersion = fallback.PortfolioHealthPolicyVersion, investorPolicyId = InvestorPolicy.Current.PolicyId, investorPolicyVersion = InvestorPolicy.Current.PolicyVersion, assessmentVersions = fallback.AssessmentVersions, evidenceCount = fallback.Evidence.Count, fallbackReason = code, usedFallback = true, generatedAt = DateTimeOffset.UtcNow, classAllocations = applied.ClassAllocations }));
        guidance.ReplaceAllocations(applied.Allocations.Select(allocation => new PersistedContributionGuidanceAllocation(
            guidance.Id, allocation.AssetId!.Value, allocation.Amount, allocation.Reason, 0, JsonSerializer.Serialize(allocation.Evidence))));
    }

    private static (string Reason, string Code) FallbackReason(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } => ("A cota da IA foi excedida; a distribuição foi calculada pela estratégia e pelas posições atuais da carteira.", "ai-quota-exceeded"),
        TimeoutException or TaskCanceledException => ("A IA não respondeu dentro do prazo; a distribuição foi calculada pela estratégia e pelas posições atuais da carteira.", "ai-timeout"),
        InvalidOperationException { Message: not null } ex when ex.Message.StartsWith("A IA", StringComparison.Ordinal) => ("A IA respondeu em formato incompatível com as regras do Prumo; a distribuição foi calculada pela estratégia e pelas posições atuais da carteira.", "ai-invalid-allocation"),
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

    private static ContributionScenarioOption[] BuildScenarioOptions(PortfolioDecision decision, ContributionGuidance baseline, IReadOnlyList<ContributionCandidate> candidates)
    {
        var options = new List<ContributionScenarioOption>();
        if (decision.RecommendedScenario is null)
        {
            if (decision.DecisionType == PortfolioDecisionType.NoAction)
                return new[]
                {
                    new ContributionScenarioOption(
                        "scenario:no-action",
                        Array.Empty<CandidateAllocation>(),
                        0m,
                        decision.Reasons.FirstOrDefault() ?? "Não há uma necessidade de alocação suficientemente relevante agora.",
                        null,
                        new[] { "A carteira está dentro da tolerância definida para as alocações-alvo." },
                        0m,
                        0m)
                };

            var candidateKeys = candidates.Select(x => x.CandidateKey).ToHashSet();
            var eligible = candidates.Where(x => !x.HasBlock && x.ClassBudget >= baseline.ContributionAmount)
                .OrderByDescending(x => x.DecisionScore).ThenBy(x => x.RecentContributionCount).ThenBy(x => x.Ticker, StringComparer.Ordinal)
                .Take(3).ToArray();
            options.AddRange(eligible.Select((candidate, index) => new ContributionScenarioOption(
                $"scenario:candidate:{index + 1}",
                new[] { new CandidateAllocation(candidate.Identity, baseline.ContributionAmount) },
                0m,
                $"{candidate.Ticker} é um candidato elegível para o aporte dentro dos limites conhecidos.",
                decision.PrimaryNeed?.AssetClass.ToString(),
                new[] { "A seleção considera os candidatos válidos, os bloqueios do Radar e os limites da carteira." },
                0m,
                0m)));
            if (options.Count == 0)
            {
                var baselineAllocations = baseline.Allocations
                    .Where(x => x.AssetId is not null)
                    .Select(x => new CandidateAllocation(CandidateIdentity.Known(x.AssetId!.Value), x.Amount))
                    .Where(x => candidateKeys.Contains(x.Key))
                    .ToArray();
                if (baselineAllocations.Length > 0)
                    options.Add(new ContributionScenarioOption("scenario:baseline-partial", baselineAllocations, 0m, "Distribuição parcial segura calculada pelas regras da carteira.", decision.PrimaryNeed?.AssetClass.ToString(), new[] { "Preserva apenas a parte do aporte que respeita os limites conhecidos." }, 0m, 0m));
            }
            if (options.Count == 0)
                options.Add(new ContributionScenarioOption("scenario:no-action", Array.Empty<CandidateAllocation>(), 0m, "Não há candidato elegível para este aporte.", decision.PrimaryNeed?.AssetClass.ToString(), new[] { "Não existem ativos válidos e desbloqueados para recomendar." }, 0m, 0m));
            return options.ToArray();
        }
        var primaryNeed = decision.PrimaryNeed?.AssetClass.ToString();
        var summary = decision.RebalanceMode == ContributionRebalanceMode.Maintenance
            ? "Cenário de manutenção: não há classe prioritária; compara benefícios marginais entre destinos elegíveis."
            : "Cenário determinístico de referência.";
        options.Add(ToScenarioOption("scenario:deterministic", decision.RecommendedScenario, summary, primaryNeed));
        options.AddRange(decision.AlternativeScenarios.Select((scenario, index) =>
            ToScenarioOption($"scenario:alternative:{index + 1}", scenario, "Alternativa determinística válida.", primaryNeed)));
        return options.ToArray();
    }

    private static ContributionScenarioOption ToScenarioOption(string key, ContributionScenario scenario, string summary, string? primaryNeed)
    {
        var structuralReasons = scenario.Reasons
            .Concat(scenario.ExposureImpacts?.Where(x => x.AfterPercentage < x.BeforePercentage)
                .Select(x => $"Reduz a exposição conhecida em {x.Dimension} ({x.Key}).") ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new(key, scenario.Allocations, scenario.Improvement, summary, primaryNeed, structuralReasons, scenario.DeviationBefore, scenario.DeviationAfter);
    }

    private async Task<(AllocationProposal Proposal, IReadOnlyList<CandidateAllocation> Allocations, int RetryCount)> ProposeValidatedAllocationAsync(
        IContributionIntelligenceProvider provider,
        Guid portfolioId,
        decimal amount,
        IReadOnlyList<ContributionCandidate> candidates,
        IReadOnlyList<ContributionScenarioOption> deterministicReferences,
        InvestorPolicy investorPolicy,
        CancellationToken cancellationToken)
    {
        Exception? last = null;
        string? rejectionReason = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var proposal = await provider.ProposeAllocationAsync(portfolioId, amount, candidates, deterministicReferences, investorPolicy, rejectionReason, cancellationToken);
                var allocations = AllocationProposalValidator.Validate(proposal, amount, candidates);
                if (!HasGroundedReason(proposal))
                    throw new InvalidOperationException("A IA retornou uma justificativa insuficiente para a alocação proposta.");
                if (attempt > 0) metrics.RecordAiOutcome("retry-succeeded");
                return (proposal, allocations, attempt);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                last = exception;
                rejectionReason = exception.Message;
                if (attempt == 0)
                {
                    metrics.RecordAiOutcome("retry");
                    logger.LogWarning(exception, "Resposta da IA não passou na validação; uma nova tentativa será feita");
                }
            }
        }

        metrics.RecordAiOutcome("fallback");
        throw last ?? new InvalidOperationException("A IA não retornou uma alocação válida.");
    }

    private static bool HasGroundedReason(AllocationProposal proposal)
    {
        var text = $"{proposal.Reason} {string.Join(" ", proposal.PrimaryFactors)}".Trim().ToLowerInvariant();
        if (string.Equals(proposal.DecisionType, "NoAction", StringComparison.OrdinalIgnoreCase)) return text.Length >= 12;
        if (text.Length < 12) return false;
        var criteria = new[] { "classe", "target", "desvio", "divers", "concent", "aporte", "assessment", "bloque", "exposi", "posi", "recente" };
        return criteria.Any(text.Contains);
    }

    private static string AllocationMode(IReadOnlyList<CandidateAllocation> allocations, IReadOnlyList<ContributionScenarioOption> references)
    {
        static bool Same(IReadOnlyList<CandidateAllocation> left, IReadOnlyList<CandidateAllocation> right)
            => left.Count == right.Count && left.OrderBy(x => x.Key.Value, StringComparer.Ordinal).SequenceEqual(right.OrderBy(x => x.Key.Value, StringComparer.Ordinal));
        if (references.Count > 0 && Same(allocations, references[0].Allocations)) return "baseline";
        if (references.Any(reference => Same(allocations, reference.Allocations))) return "modified-baseline";
        return "ai-composition";
    }

    private async Task<(ContributionScenarioSelection Selection, ContributionScenarioOption Selected, int RetryCount)> SelectValidatedScenarioAsync(
        IContributionIntelligenceProvider provider,
        Guid portfolioId,
        decimal amount,
        IReadOnlyList<ContributionScenarioOption> scenarios,
        IReadOnlyList<ContributionCandidate> candidates,
        InvestorPolicy investorPolicy,
        ContributionRebalanceMode rebalanceMode,
        CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var selection = await provider.SelectScenarioAsync(portfolioId, amount, scenarios, candidates, investorPolicy, cancellationToken);
                var selected = ContributionScenarioValidator.Select(selection, scenarios, amount, candidates);
                if (!HasGroundedReason(selection, selected))
                    throw new InvalidOperationException("A IA retornou uma justificativa insuficiente para o cenário selecionado.");
                if (rebalanceMode == ContributionRebalanceMode.Required && selected.Improvement + 0.01m < scenarios[0].Improvement)
                    throw new InvalidOperationException("A IA retornou um cenário inferior ao cenário-base seguro.");

                if (attempt > 0) metrics.RecordAiOutcome("retry-succeeded");
                else metrics.RecordAiOutcome("selected");
                return (selection, selected, attempt);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                last = exception;
                if (attempt == 0)
                {
                    metrics.RecordAiOutcome("retry");
                    logger.LogWarning(exception, "Resposta da IA não passou na validação; uma nova tentativa será feita");
                }
            }
        }

        metrics.RecordAiOutcome("fallback");
        throw last ?? new InvalidOperationException("A IA não retornou um cenário válido.");
    }

    private static (ContributionScenarioSelection Selection, ContributionScenarioOption Selected, int RetryCount) SingleScenarioSelection(ContributionScenarioOption scenario)
    {
        var selection = new ContributionScenarioSelection(
            scenario.ScenarioKey,
            scenario.Summary,
            Array.Empty<string>(),
            ContributionIntelligenceAvailability.PartialIntelligence,
            new("R17.8", "portfolio-rules", ContributionIntelligencePrompt.Version, "deterministic-single-scenario", "none", DateTimeOffset.UtcNow));
        return (selection, scenario, 0);
    }

    private static bool HasGroundedReason(ContributionScenarioSelection selection, ContributionScenarioOption scenario)
    {
        var text = $"{selection.Reason} {string.Join(" ", selection.Considerations)} {scenario.Summary}".Trim().ToLowerInvariant();
        if (text.Length < 12) return false;
        var criteria = new[] { "classe", "target", "desvio", "divers", "concent", "aporte", "assessment", "bloque", "exposi", "segur", "candidat" };
        return criteria.Any(text.Contains);
    }

    private static PersistedContributionGuidanceAllocation[] ToPersistedAllocations(Guid guidanceId, ContributionScenarioOption scenario, IReadOnlyList<ContributionCandidate> candidates)
        => scenario.Allocations.Select(allocation => PersistedAllocation(
            guidanceId,
            allocation,
            candidates.Single(candidate => candidate.CandidateKey == allocation.Key),
            1)).ToArray();

    private static PersistedContributionGuidanceAllocation[] ToPersistedAllocations(Guid guidanceId, IReadOnlyList<CandidateAllocation> allocations, IReadOnlyList<ContributionCandidate> candidates, string confidence)
    {
        var numericConfidence = confidence.ToLowerInvariant() switch { "high" => .9m, "medium" => .6m, _ => .3m };
        return allocations.Select(allocation => PersistedAllocation(
            guidanceId,
            allocation,
            candidates.Single(candidate => candidate.CandidateKey == allocation.Key),
            numericConfidence)).ToArray();
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
