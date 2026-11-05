using System.Text.Json;
using System.Diagnostics;
using Prumo.Infrastructure.Ai;
using Microsoft.EntityFrameworkCore;
using Prumo.Application.Contributions;
using Prumo.Domain.Entities;
using Prumo.Infrastructure.Persistence;
using Prumo.Application.Abstractions;
using Prumo.Domain.Enums;
using System.Reflection;

namespace Prumo.Worker;

public sealed class ContributionGuidanceWorker(IServiceScopeFactory scopes, ILogger<ContributionGuidanceWorker> logger, WorkerMetrics metrics) : BackgroundService
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(30);
    private static readonly string WorkerVersion = typeof(ContributionGuidanceWorker).Assembly.GetName().Version?.ToString() ?? "unknown";
    private static readonly string WorkerBuild = typeof(ContributionGuidanceWorker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid? claimedJobId = null;
            var claimedAttempt = 0;
            try
            {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PrumoDbContext>(); var job = await ClaimNextAsync(db, stoppingToken);
                if (job is null) { metrics.SetBacklog("ContributionGuidance", 0); await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); continue; }
                claimedJobId = job.Id;
                claimedAttempt = job.Attempts;
                var stopwatch = Stopwatch.StartNew();
                var backlog = await db.ContributionGuidanceJobs.CountAsync(x => x.Status == ContributionGuidanceJobStatus.Queued, stoppingToken);
                metrics.SetBacklog("ContributionGuidance", backlog);
                try
                {
                    var guidance = await db.ContributionGuidances.Include(x => x.Allocations).SingleAsync(x => x.Id == job.ContributionGuidanceId, stoppingToken);
                    var workItem = DeserializeWorkItem(job.CandidatesJson);
                    workItem = workItem with
                    {
                        Candidates = workItem.Candidates.Select(candidate => candidate with
                        {
                            CurrentAssessments = candidate.CandidateIntelligence?.Assessments ?? candidate.CurrentAssessments
                        }).ToArray()
                    };
                    var investorPolicy = InvestorPolicy.Current;
                    var deterministicDecision = new PortfolioDecisionService().DecideDeterministically(guidance.RequestedAmount, workItem.Candidates, investorPolicy);
                    var usedFallback = false;
                    var executionPath = "normal";
                    var deterministicOptions = BuildScenarioOptions(deterministicDecision, workItem.Baseline, workItem.Candidates);
                    var eligibleCandidates = workItem.Candidates.Where(candidate => !candidate.HasBlock && candidate.ClassBudget > 0).ToArray();
                    var configuredProvider = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiProviderOptions>>().Value.Provider;
                    var deterministicMode = !configuredProvider.Equals("Gemini", StringComparison.OrdinalIgnoreCase)
                        && !configuredProvider.Equals("Http", StringComparison.OrdinalIgnoreCase);
                    if (deterministicMode)
                    {
                        usedFallback = true;
                        executionPath = "deterministic-configured";
                        ApplyFallback(guidance, workItem.Baseline,
                            "A orientação foi calculada pelo motor de regras; a análise de aportes por IA está desativada na configuração.",
                            "deterministic-configured", workItem, executionPath);
                    }
                    else if (eligibleCandidates.Length == 0)
                    {
                        usedFallback = true;
                        executionPath = "deterministic-fallback";
                        ApplyFallback(
                            guidance,
                            workItem.Baseline,
                            deterministicDecision.Reasons.FirstOrDefault() ?? "Não há candidato elegível dentro dos limites atuais.",
                            "no-eligible-candidates", workItem, executionPath);
                    }
                    else
                    {
                        try
                        {
                            var provider = scope.ServiceProvider.GetRequiredService<IContributionIntelligenceProvider>();
                            var (proposal, allocations, retryCount) = await ProposeValidatedAllocationAsync(
                                provider, job.PortfolioId,
                                guidance.RequestedAmount, workItem.Candidates, deterministicOptions, investorPolicy, stoppingToken);
                            var initialProjection = CompositionProjectionEvaluator.Project(workItem.Candidates, allocations);
                            AllocationReview? review = null;
                            AllocationReviewResolution reviewResolution;
                            try
                            {
                                review = await provider.ReviewAllocationAsync(job.PortfolioId, guidance.RequestedAmount, workItem.Candidates, deterministicOptions, investorPolicy, initialProjection, stoppingToken);
                                reviewResolution = AllocationReviewResolver.Resolve(allocations, review, guidance.RequestedAmount, workItem.Candidates);
                                if (!reviewResolution.ReviewAccepted)
                                    logger.LogWarning("Revisão da composição para o job de guidance {JobId} não foi aceita; a proposta inicial válida foi preservada: {Warning}", job.Id, reviewResolution.Warning);
                            }
                            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                executionPath = "review-fallback";
                                reviewResolution = new AllocationReviewResolution(allocations, false, false, "review-unavailable-kept-initial", $"A revisão pós-projeção não ficou disponível ({ex.Message}); a proposta inicial válida foi preservada.");
                                logger.LogWarning(ex, "Revisão pós-projeção indisponível para o job de guidance {JobId}; a proposta inicial válida foi preservada", job.Id);
                            }

                            var finalAllocations = reviewResolution.FinalAllocations;
                            var finalProjection = reviewResolution.CompositionChangedAfterProjection
                                ? CompositionProjectionEvaluator.Project(workItem.Candidates, finalAllocations)
                                : initialProjection;
                            var finalMetadata = reviewResolution.ReviewAccepted && review is not null ? review.Metadata : proposal.Metadata;
                            var finalReason = reviewResolution.ReviewAccepted && !string.IsNullOrWhiteSpace(review?.Reason) ? review.Reason : proposal.Reason;
                            finalReason = ContributionText.NormalizeCandidateKeys(ContributionText.NormalizeAssetClassNames(finalReason), workItem.Candidates);
                            var allocationMode = AllocationMode(finalAllocations, deterministicOptions);
                            metrics.RecordAiOutcome(allocationMode);
                            metrics.RecordAiOutcome($"composition-review-{reviewResolution.Outcome}");
                            var proposalContext = ContributionIntelligencePrompt.BuildAllocationProposal(job.PortfolioId, guidance.RequestedAmount, workItem.Candidates, deterministicOptions, investorPolicy);
                            var reviewContext = ContributionIntelligencePrompt.BuildAllocationReview(job.PortfolioId, guidance.RequestedAmount, workItem.Candidates, deterministicOptions, investorPolicy, initialProjection);
                            var proposalEventCount = workItem.Candidates.Sum(x => x.CandidateIntelligence?.RecentEvents.Count ?? 0);
                            var reviewEventCount = proposalEventCount;
                            guidance.UpdateIntelligence(finalMetadata.Provider, finalMetadata.Model, proposal.Availability.ToString(), finalReason, JsonSerializer.Serialize(new
                            {
                                engineVersion = finalMetadata.EngineVersion,
                                policyVersion = finalMetadata.PolicyVersion,
                                promptVersion = finalMetadata.PromptVersion,
                                generatedAt = finalMetadata.GeneratedAt,
                                deterministicBaselineScenarioKey = deterministicOptions.FirstOrDefault()?.ScenarioKey,
                                usedFallback = proposal.Metadata.UsedFallback || review?.Metadata.UsedFallback == true,
                                workerVersion = WorkerVersion,
                                workerBuild = WorkerBuild,
                                executionPath,
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
                                candidateIntelligenceCandidateCount = workItem.Candidates.Count(x => x.CandidateIntelligence is not null),
                                candidateIntelligencePayloadLimited = workItem.CandidateIntelligencePayloadLimited,
                                candidatePayloadOriginalChars = workItem.CandidatePayloadOriginalChars,
                                candidatePayloadProjectedChars = workItem.CandidatePayloadProjectedChars,
                                candidatePayloadPersistedChars = workItem.CandidatePayloadPersistedChars,
                                candidatePayloadReductionPercent = workItem.CandidatePayloadReductionPercent,
                                candidatePayloadTruncatedEventCount = workItem.CandidatePayloadTruncatedEventCount,
                                candidatePayloadTruncatedSummaryCount = workItem.CandidatePayloadTruncatedSummaryCount,
                                candidateIntelligenceCoverage = workItem.Candidates.ToDictionary(x => x.CandidateKey.Value, x => x.CandidateIntelligence?.Coverage.ToString() ?? "Unavailable"),
                                candidateIntelligenceSectionStatuses = workItem.Candidates.ToDictionary(x => x.CandidateKey.Value, x => new
                                {
                                    fundamentals = x.CandidateIntelligence?.Fundamentals.Status.ToString() ?? "Unavailable",
                                    valuation = x.CandidateIntelligence?.Valuation.Status.ToString() ?? "Unavailable",
                                    incomeQuality = x.CandidateIntelligence?.IncomeQuality.Status.ToString() ?? "Unavailable",
                                    events = x.CandidateIntelligence?.RecentEvents.Count > 0 ? "Available" : "Unavailable",
                                    assessments = x.CandidateIntelligence?.Assessments.Count > 0 ? "Available" : "Unavailable"
                                }),
                                candidateIntelligenceEvidenceReferenceCount = workItem.Candidates.Sum(x => x.CandidateIntelligence?.Evidence.Count ?? 0),
                                candidateIntelligenceAssessmentCount = workItem.Candidates.Sum(x => x.CandidateIntelligence?.Assessments.Count ?? 0),
                                candidateIntelligenceDocumentEvidenceCount = workItem.Candidates.Sum(x => x.CandidateIntelligence?.Evidence.Count(e => e.Type == "Document") ?? 0),
                                primaryFactors = proposal.PrimaryFactors,
                                confidence = proposal.Confidence,
                                proposalDecisionType = proposal.DecisionType,
                                initialAiAllocation = initialProjection.ProposedAllocation,
                                initialProjection,
                                proposalPromptChars = proposalContext.Length,
                                reviewPromptChars = reviewContext.Length,
                                storagePayloadChars = workItem.CandidatePayloadPersistedChars,
                                proposalContextChars = proposalContext.Length,
                                reviewContextChars = reviewContext.Length,
                                proposalMetricEvidenceCount = workItem.Candidates.Sum(x => x.CandidateIntelligence?.MetricEvidence.Count ?? 0),
                                reviewMetricEvidenceCount = workItem.Candidates.Sum(x => x.CandidateIntelligence?.MetricEvidence.Count ?? 0),
                                metricEvidenceStatusCounts = MetricEvidenceStatusCounts(workItem.Candidates),
                                metricEvidenceConflictCount = workItem.Candidates.Sum(x => x.CandidateIntelligence?.MetricEvidence.Count(e => e.Status == MetricEvidenceResolutionStatus.Conflict) ?? 0),
                                proposalEventCount,
                                reviewEventCount,
                                storagePayloadLimited = false,
                                proposalContextLimited = false,
                                reviewContextLimited = false,
                                reviewDecision = review?.Decision ?? "unavailable",
                                reviewOutcome = reviewResolution.Outcome,
                                reviewAccepted = reviewResolution.ReviewAccepted,
                                reviewReason = review?.Reason,
                                reviewConsiderations = review?.Considerations ?? Array.Empty<string>(),
                                reviewWarning = reviewResolution.Warning,
                                reviewProvider = review?.Metadata.Provider,
                                reviewModel = review?.Metadata.Model,
                                proposalConfiguredModel = proposal.Metadata.ConfiguredModel,
                                proposalFallbackModel = proposal.Metadata.FallbackModel,
                                proposalUsedFallback = proposal.Metadata.UsedFallback,
                                reviewConfiguredModel = review?.Metadata.ConfiguredModel,
                                reviewFallbackModel = review?.Metadata.FallbackModel,
                                reviewUsedFallback = review?.Metadata.UsedFallback,
                                reviewPromptVersion = review?.Metadata.PromptVersion,
                                finalAllocation = finalProjection.ProposedAllocation,
                                finalProjection,
                                compositionChangedAfterProjection = reviewResolution.CompositionChangedAfterProjection
                            }));
                            guidance.ReplaceAllocations(ToPersistedAllocations(guidance.Id, finalAllocations, workItem.Candidates, proposal.Confidence));
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            usedFallback = true;
                            executionPath = "proposal-fallback";
                            ApplyFallback(guidance, workItem.Baseline, FallbackReason(ex).Reason, FallbackReason(ex).Code, workItem, executionPath);
                            logger.LogWarning(ex, "Resposta da IA não utilizável para o job de guidance {JobId}; fallback determinístico aplicado", job.Id);
                        }
                    }
                    AppendProjectedPortfolioReason(guidance, workItem);
                    var publication = ContributionGuidancePublication.From(guidance);
                    var published = await PublishAsync(job.Id, claimedAttempt, publication, stoppingToken);
                    if (!published)
                    {
                        logger.LogWarning("Resultado do job de guidance {JobId} descartado porque a tentativa não possui mais o lease", job.Id);
                        continue;
                    }
                    metrics.RecordCompleted("ContributionGuidance", stopwatch.Elapsed);
                    logger.LogInformation("Job de guidance {JobId} concluído em {DurationMs} ms; backlog={Backlog}; fallback={UsedFallback}", job.Id, stopwatch.Elapsed.TotalMilliseconds, backlog, usedFallback);
                }
                catch (Exception ex)
                {
                    metrics.RecordFailed("ContributionGuidance", stopwatch.Elapsed);
                    await RecoverClaimedJobAsync(job.Id, claimedAttempt, ex.Message, stoppingToken);
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
                    await RecoverClaimedJobAsync(claimedJobId.Value, claimedAttempt, ex.Message, stoppingToken);
                }
                logger.LogError(ex, "Erro inesperado no polling de jobs de guidance; nova tentativa em {RetryDelay}", TimeSpan.FromSeconds(10));
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private static void ApplyFallback(PersistedContributionGuidance guidance, ContributionGuidance fallback, string reason, string code, ContributionGuidanceWorkItem? workItem = null, string executionPath = "deterministic-fallback")
    {
        var applied = ContributionIntelligenceApplier.ApplyFallback(fallback, reason);
        guidance.UpdateIntelligence(
            "deterministic-fallback",
            "portfolio-rules",
            ContributionIntelligenceAvailability.InsufficientIntelligence.ToString(),
            applied.Reasons.FirstOrDefault() ?? reason,
            JsonSerializer.Serialize(new { engineVersion = "R17.8", policyVersion = fallback.PortfolioHealthPolicyVersion, workerVersion = WorkerVersion, workerBuild = WorkerBuild, executionPath, investorPolicyId = InvestorPolicy.Current.PolicyId, investorPolicyVersion = InvestorPolicy.Current.PolicyVersion, assessmentVersions = fallback.AssessmentVersions, evidenceCount = fallback.Evidence.Count, candidateIntelligenceCandidateCount = workItem?.Candidates.Count(x => x.CandidateIntelligence is not null) ?? 0, candidateIntelligencePayloadLimited = workItem?.CandidateIntelligencePayloadLimited ?? false, candidatePayloadOriginalChars = workItem?.CandidatePayloadOriginalChars ?? 0, candidatePayloadProjectedChars = workItem?.CandidatePayloadProjectedChars ?? 0, candidatePayloadPersistedChars = workItem?.CandidatePayloadPersistedChars ?? 0, candidatePayloadReductionPercent = workItem?.CandidatePayloadReductionPercent ?? 0, candidatePayloadTruncatedEventCount = workItem?.CandidatePayloadTruncatedEventCount ?? 0, candidatePayloadTruncatedSummaryCount = workItem?.CandidatePayloadTruncatedSummaryCount ?? 0, candidateIntelligenceEvidenceReferenceCount = workItem?.Candidates.Sum(x => x.CandidateIntelligence?.Evidence.Count ?? 0), candidateIntelligenceAssessmentCount = workItem?.Candidates.Sum(x => x.CandidateIntelligence?.Assessments.Count ?? 0), candidateIntelligenceDocumentEvidenceCount = workItem?.Candidates.Sum(x => x.CandidateIntelligence?.Evidence.Count(e => e.Type == "Document") ?? 0), proposalContextChars = (int?)null, reviewContextChars = (int?)null, proposalEventCount = (int?)null, reviewEventCount = (int?)null, storagePayloadChars = workItem?.CandidatePayloadPersistedChars, storagePayloadLimited = false, proposalContextLimited = false, reviewContextLimited = false, fallbackReason = code, usedFallback = true, generatedAt = DateTimeOffset.UtcNow, classAllocations = applied.ClassAllocations }));
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

    private static ContributionGuidanceWorkItem DeserializeWorkItem(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("positions", out _))
        {
            var projected = JsonSerializer.Deserialize<PersistedContributionGuidanceWorkItem>(json)
                ?? throw new InvalidOperationException("Contexto projetado da orientação de aporte inválido.");
            return projected.ToRuntime();
        }

        return JsonSerializer.Deserialize<ContributionGuidanceWorkItem>(json)
            ?? throw new InvalidOperationException("Contexto da orientação de aporte inválido.");
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

    internal static async Task<ContributionGuidanceJob?> ClaimNextAsync(PrumoDbContext db, CancellationToken ct, DateTimeOffset? now = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var staleAt = (now ?? DateTimeOffset.UtcNow).Subtract(Lease);
        var queued = (int)ContributionGuidanceJobStatus.Queued;
        var processing = (int)ContributionGuidanceJobStatus.Processing;
        while (true)
        {
            var job = await db.ContributionGuidanceJobs
            .FromSqlInterpolated($"""
                SELECT *
                FROM "ContributionGuidanceJobs"
                WHERE "Status" = {queued}
                   OR ("Status" = {processing} AND "StartedAt" <= {staleAt})
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

            if (job.Attempts >= MaxAttempts)
            {
                job.Fail("Limite de tentativas excedido após expiração do lease.");
                await db.SaveChangesAsync(ct);
                continue;
            }

            job.Start();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return job;
        }
    }

    internal async Task RecoverClaimedJobAsync(Guid jobId, int attempt, string error, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PrumoDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var job = await db.ContributionGuidanceJobs
                .FromSqlInterpolated($"SELECT * FROM \"ContributionGuidanceJobs\" WHERE \"Id\" = {jobId} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
            if (job?.Status != ContributionGuidanceJobStatus.Processing || job.Attempts != attempt) return;
            job.RetryOrFail(error, MaxAttempts);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception recoveryException)
        {
            logger.LogError(recoveryException, "Falha ao recuperar o job de guidance {JobId} após erro inesperado", jobId);
        }
    }

    internal async Task<bool> PublishAsync(Guid jobId, int attempt, ContributionGuidancePublication publication, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PrumoDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var job = await db.ContributionGuidanceJobs
            .FromSqlInterpolated($"SELECT * FROM \"ContributionGuidanceJobs\" WHERE \"Id\" = {jobId} FOR UPDATE")
            .SingleAsync(ct);
        if (job.Status != ContributionGuidanceJobStatus.Processing || job.Attempts != attempt)
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

    internal sealed record ContributionGuidancePublication(
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

    internal sealed record ContributionGuidanceAllocationPublication(
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

    private static IReadOnlyDictionary<string, int> MetricEvidenceStatusCounts(IReadOnlyList<ContributionCandidate> candidates)
        => candidates.SelectMany(x => x.CandidateIntelligence?.Freshness?.ByMetric.Values ?? Array.Empty<MetricApplicability>())
            .GroupBy(x => x.ToString())
            .ToDictionary(x => x.Key, x => x.Count());
}
