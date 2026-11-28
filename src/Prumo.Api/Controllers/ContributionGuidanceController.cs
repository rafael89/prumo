using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prumo.Application.Contributions;
using Prumo.Application.Portfolio;
using Prumo.Api.Services;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Reconciliation;
using Prumo.Infrastructure.MarketData;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/contribution-guidance")]
public sealed class ContributionGuidanceController(PrumoDbContext db, PortfolioAssessmentCurrentService current, TransactionReconciliationService? canonical = null, AssetMetadataCatalogSyncService? assetMetadataCatalog = null, ContributionContextBuilder? contextBuilder = null) : ControllerBase
{
    private static readonly JsonSerializerOptions PersistedJson = new() { PropertyNameCaseInsensitive = true };
    private const string ContributionEngineVersion = "R4-9.2";

    [HttpPost]
    public async Task<ActionResult<object>> Generate(Guid portfolioId, ContributionRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0) return BadRequest("O valor do aporte deve ser positivo.");

        var portfolio = await db.Portfolios.WithContributionFinancials(db)
            .SingleOrDefaultAsync(x => x.Id == portfolioId, ct);
        if (portfolio is null) return NotFound();
        if (assetMetadataCatalog is not null) await assetMetadataCatalog.ApplyAsync(ct);

        var targets = await db.TargetAllocations.AsNoTracking().Where(x => x.PortfolioId == portfolioId).ToListAsync(ct);
        if (targets.Count == 0) return Conflict(new { status = ContributionGuidanceStatus.NotConfigured.ToString() });
        if (portfolio.Positions.Any(x => !x.CurrentValue.HasValue)) return BadRequest("Não há preço válido para todas as posições existentes.");

        var snapshot = PortfolioSnapshotKeyCalculator.Calculate(portfolio);
        var targetKey = TargetAllocationKeyCalculator.Calculate(targets)!;
        var amount = decimal.Round(request.Amount, 2);
        var context = await (contextBuilder ?? new ContributionContextBuilder(db, current, canonical)).BuildAsync(portfolio, targets, amount, ct);
        var intelligenceContextKey = context.IntelligenceContextKey;
        var existing = await db.ContributionGuidances.AsNoTracking().Include(x => x.Allocations)
            .Where(x => x.PortfolioId == portfolioId
                && x.RequestedAmount == amount
                && x.PortfolioSnapshotKey == snapshot
                && x.TargetAllocationKey == targetKey
                && x.IntelligenceContextKey == intelligenceContextKey
                && x.EngineVersion == ContributionEngineVersion)
            .OrderByDescending(x => x.GeneratedAt).FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            var existingStatus = await db.ContributionGuidanceJobs.AsNoTracking()
                .Where(x => x.ContributionGuidanceId == existing.Id).OrderByDescending(x => x.CreatedAt)
                .Select(x => (ContributionGuidanceJobStatus?)x.Status).FirstOrDefaultAsync(ct);
            // Completed guidance is intentionally not reused: each new simulation
            // must consult the configured intelligence provider again.
            if (existingStatus is ContributionGuidanceJobStatus.Queued or ContributionGuidanceJobStatus.Processing)
                return Accepted(PendingResponse(existing.Id, existing.RequestedAmount, existingStatus.Value, true));
        }

        var persisted = new PersistedContributionGuidance(portfolioId, amount, snapshot, targetKey, intelligenceContextKey, ContributionEngineVersion, "pending-ai", "pending", "Pending", "Análise em andamento: o Prumo está avaliando os candidatos e os limites da carteira.", "{}");
        db.ContributionGuidances.Add(persisted);
        var workItem = ContributionContextBuilder.BuildPersistableWorkItem(context.Baseline, context.Positions, context.Candidates);
        persisted.UpdateIntelligence("pending-ai", "pending", "Pending", "Análise em andamento: o Prumo está avaliando os candidatos e os limites da carteira.",
            JsonSerializer.Serialize(new
            {
                candidateIntelligencePayloadLimited = workItem.CandidateIntelligencePayloadLimited,
                candidatePayloadOriginalChars = workItem.CandidatePayloadOriginalChars,
                candidatePayloadProjectedChars = workItem.CandidatePayloadProjectedChars,
                candidatePayloadPersistedChars = workItem.CandidatePayloadPersistedChars,
                candidatePayloadReductionPercent = workItem.CandidatePayloadReductionPercent,
                candidatePayloadTruncatedEventCount = workItem.CandidatePayloadTruncatedEventCount,
                candidatePayloadTruncatedSummaryCount = workItem.CandidatePayloadTruncatedSummaryCount,
                storagePayloadChars = workItem.CandidatePayloadPersistedChars,
                proposalContextChars = 0,
                reviewContextChars = 0,
                proposalEventCount = 0,
                reviewEventCount = 0,
                storagePayloadLimited = false,
                proposalContextLimited = false,
                reviewContextLimited = false,
                generatedAt = DateTimeOffset.UtcNow
            }));
        var job = new ContributionGuidanceJob(portfolioId, persisted.Id, JsonSerializer.Serialize(workItem));
        db.ContributionGuidanceJobs.Add(job);
        await db.SaveChangesAsync(ct);

        return Accepted(PendingResponse(persisted.Id, amount, job.Status, false));
    }

    [HttpGet("{guidanceId:guid}")]
    public async Task<ActionResult<object>> Get(Guid portfolioId, Guid guidanceId, CancellationToken ct)
    {
        var item = await db.ContributionGuidances.AsNoTracking().Include(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.Id == guidanceId && x.PortfolioId == portfolioId, ct);
        if (item is null) return NotFound();

        var status = await db.ContributionGuidanceJobs.AsNoTracking().Where(x => x.ContributionGuidanceId == guidanceId)
            .OrderByDescending(x => x.CreatedAt).Select(x => (ContributionGuidanceJobStatus?)x.Status).FirstOrDefaultAsync(ct);
        if (status is not ContributionGuidanceJobStatus.Completed)
            return Ok(PendingResponse(item.Id, item.RequestedAmount, status ?? ContributionGuidanceJobStatus.Failed, true));

        return Ok(await CompletedResponseAsync(portfolioId, item, ct));
    }

    [HttpGet("latest")]
    public async Task<ActionResult<object>> GetLatest(Guid portfolioId, CancellationToken ct)
    {
        var item = await db.ContributionGuidances.AsNoTracking().Include(x => x.Allocations)
            .Where(x => x.PortfolioId == portfolioId)
            .OrderByDescending(x => x.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        if (item is null) return NotFound();

        var status = await db.ContributionGuidanceJobs.AsNoTracking()
            .Where(x => x.ContributionGuidanceId == item.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => (ContributionGuidanceJobStatus?)x.Status)
            .FirstOrDefaultAsync(ct);
        if (status is ContributionGuidanceJobStatus.Completed)
            return Ok(await CompletedResponseAsync(portfolioId, item, ct));

        return Ok(PendingResponse(item.Id, item.RequestedAmount, status ?? ContributionGuidanceJobStatus.Failed, true));
    }

    private async Task<object> CompletedResponseAsync(Guid portfolioId, PersistedContributionGuidance item, CancellationToken ct)
    {
        var executedAssetIds = await db.PortfolioTransactions.AsNoTracking()
            .Where(x => x.ContributionGuidanceId == item.Id)
            .SelectMany(x => x.Items).Select(x => x.AssetId).Distinct().ToListAsync(ct);
        var positions = await db.PortfolioPositions.AsNoTracking().Include(x => x.Asset)
            .Where(x => x.PortfolioId == portfolioId).ToDictionaryAsync(x => x.AssetId, ct);
        var assets = await db.Assets.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var allocations = item.Allocations.Select(x => new
        {
            x.AssetId,
            x.Origin,
            x.CandidateKey,
            executed = x.AssetId.HasValue ? executedAssetIds.Contains(x.AssetId.Value)
                : assets.Values.Any(asset => asset.Ticker == x.Ticker && executedAssetIds.Contains(asset.Id)),
            ticker = x.Ticker ?? (x.AssetId.HasValue ? assets.GetValueOrDefault(x.AssetId.Value)?.Ticker ?? positions.GetValueOrDefault(x.AssetId.Value)?.Asset?.Ticker : null) ?? string.Empty,
            assetClass = (int)(x.AssetClass ?? (x.AssetId.HasValue ? assets.GetValueOrDefault(x.AssetId.Value)?.AssetClass ?? positions.GetValueOrDefault(x.AssetId.Value)?.Asset?.AssetClass : null) ?? AssetClass.Other),
            x.Amount,
            reason = ContributionText.NormalizeAssetClassNames(x.Reason),
            evidence = DeserializeEvidence(x.EvidenceJson)
        }).ToArray();
        var classAllocations = ExtractClassAllocations(item.MetadataJson).Select(x => new
        {
            assetClass = (int)x.AssetClass,
            x.Amount,
            reason = ContributionText.NormalizeAssetClassNames(x.Reason)
        }).ToArray();
        var selectedAssetIds = allocations.Where(x => x.AssetId is not null).Select(x => x.AssetId!.Value).ToHashSet();
        var jobCandidates = await db.ContributionGuidanceJobs.AsNoTracking()
            .Where(x => x.ContributionGuidanceId == item.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.CandidatesJson)
            .FirstOrDefaultAsync(ct);
        var avoidedCandidates = Array.Empty<object>();
        ContributionGuidanceWorkItem? workItem = null;
        if (!string.IsNullOrWhiteSpace(jobCandidates))
        {
            workItem = DeserializeWorkItemForRead(jobCandidates);
            var classesWithoutBudget = workItem?.Candidates?.GroupBy(x => x.AssetClass)
                .Where(group => group.All(candidate => candidate.ClassBudget <= 0))
                .Select(group => group.Key).ToHashSet();
            avoidedCandidates = workItem?.Baseline?.AvoidedCandidates
                .Where(x => !selectedAssetIds.Contains(x.AssetId))
                .GroupBy(x => x.AssetId)
                .Select(x => x.First())
                .Take(5)
                .Select(x => (object)new
                {
                    x.AssetId,
                    x.Ticker,
                    assetClass = (int)x.AssetClass,
                    reason = x.ReasonType == ContributionAvoidanceReasonType.LowerPriority && classesWithoutBudget?.Contains(x.AssetClass) == true
                        ? ContributionText.ClassWithoutBudgetReason
                        : ContributionText.NormalizeAssetClassNames(x.Reason)
                })
                .ToArray() ?? Array.Empty<object>();
        }
        var decision = DeserializeDecision(item.MetadataJson);
        var allocated = allocations.Sum(x => x.Amount) + classAllocations.Sum(x => x.Amount);
        // The persisted decision describes the deterministic baseline, while the
        // worker may have selected a valid scenario afterwards. Never expose the
        // baseline NoAction label when the published guidance has allocations.
        var effectiveDecisionType = decision?.DecisionType;
        var aiGeneratedAllocation = string.Equals(ExtractSelectionMode(item.MetadataJson), "ai-constrained-allocation", StringComparison.Ordinal);
        if (allocations.Length > 0 && (aiGeneratedAllocation || string.Equals(effectiveDecisionType, "NoAction", StringComparison.OrdinalIgnoreCase)))
        {
            effectiveDecisionType = allocations.Length > 1
                ? "SplitContribution"
                : allocations[0].AssetId is Guid assetId && positions.TryGetValue(assetId, out var position) && position.CurrentValue > 0
                    ? "ReinforceExisting"
                    : "AddNewAsset";
        }
        return new
        {
            guidanceId = item.Id,
            executedAssetIds,
            status = allocations.Length == 0 && classAllocations.Length == 0 ? ContributionGuidanceStatus.NoEligibleCandidates.ToString() : ContributionGuidanceStatus.Calculated.ToString(),
            contributionAmount = item.RequestedAmount,
            requestedAmount = item.RequestedAmount,
            allocatedAmount = allocated,
            unallocatedAmount = item.RequestedAmount - allocated,
            allocationStyle = allocations.Length == 1 ? "Concentrated" : allocations.Length > 1 ? "Distributed" : "NoAllocation",
            allocations,
            classAllocations,
            avoidedCandidates,
            decision = decision is null ? null : new
            {
                decisionType = effectiveDecisionType,
                rebalanceMode = decision.RebalanceMode,
                contributionAmount = item.RequestedAmount,
                primaryNeed = decision.PrimaryNeed is null ? null : new
                {
                    assetClass = decision.PrimaryNeed.AssetClass,
                    currentPercentage = decision.PrimaryNeed.CurrentPercentage,
                    targetPercentage = decision.PrimaryNeed.TargetPercentage,
                    gapPercentagePoints = decision.PrimaryNeed.GapPercentagePoints
                },
                decisionEvidence = decision.DecisionEvidence,
                reasons = Array.Empty<string>(),
                missingData = Array.Empty<string>()
            },
            reasons = new[] { DecisionReason(decision?.DecisionEvidence), string.IsNullOrWhiteSpace(item.Reasoning) ? null : ContributionText.NormalizeAssetClassNames(item.Reasoning) }.Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray(),
            evidence = allocations.SelectMany(x => x.evidence).Distinct().ToArray(),
            analysis = new
            {
                generatedAt = item.GeneratedAt,
                engineVersion = item.EngineVersion,
                decisionEngineVersion = ExtractMetadataString(item.MetadataJson, "engineVersion") ?? item.EngineVersion,
                baselineEngineVersion = item.EngineVersion,
                provider = item.Provider,
                model = item.Model,
                policyVersion = ExtractPolicyVersion(item.MetadataJson),
                assessmentVersions = ExtractAssessmentVersions(item.MetadataJson),
                evidenceCount = ExtractEvidenceCount(item.MetadataJson, allocations.SelectMany(x => x.evidence).Distinct().Count()),
                candidateIntelligenceCandidateCount = ExtractMetadataInt(item.MetadataJson, "candidateIntelligenceCandidateCount"),
                candidateIntelligenceEvidenceReferenceCount = ExtractMetadataInt(item.MetadataJson, "candidateIntelligenceEvidenceReferenceCount"),
                candidateIntelligenceAssessmentCount = ExtractMetadataInt(item.MetadataJson, "candidateIntelligenceAssessmentCount"),
                candidateIntelligenceDocumentEvidenceCount = ExtractMetadataInt(item.MetadataJson, "candidateIntelligenceDocumentEvidenceCount"),
                candidateIntelligencePayloadLimited = ExtractMetadataBool(item.MetadataJson, "candidateIntelligencePayloadLimited"),
                candidatePayloadOriginalChars = ExtractMetadataInt(item.MetadataJson, "candidatePayloadOriginalChars"),
                candidatePayloadProjectedChars = ExtractMetadataInt(item.MetadataJson, "candidatePayloadProjectedChars"),
                candidatePayloadPersistedChars = ExtractMetadataInt(item.MetadataJson, "candidatePayloadPersistedChars"),
                candidatePayloadReductionPercent = ExtractMetadataDecimal(item.MetadataJson, "candidatePayloadReductionPercent"),
                candidatePayloadTruncatedEventCount = ExtractMetadataInt(item.MetadataJson, "candidatePayloadTruncatedEventCount"),
                candidatePayloadTruncatedSummaryCount = ExtractMetadataInt(item.MetadataJson, "candidatePayloadTruncatedSummaryCount"),
                storagePayloadChars = ExtractMetadataInt(item.MetadataJson, "storagePayloadChars"),
                proposalContextChars = ExtractMetadataNullableInt(item.MetadataJson, "proposalContextChars"),
                reviewContextChars = ExtractMetadataNullableInt(item.MetadataJson, "reviewContextChars"),
                proposalEventCount = ExtractMetadataNullableInt(item.MetadataJson, "proposalEventCount"),
                reviewEventCount = ExtractMetadataNullableInt(item.MetadataJson, "reviewEventCount"),
                workerVersion = ExtractMetadataString(item.MetadataJson, "workerVersion"),
                workerBuild = ExtractMetadataString(item.MetadataJson, "workerBuild"),
                executionPath = ExtractMetadataString(item.MetadataJson, "executionPath"),
                usedFallback = ExtractMetadataBool(item.MetadataJson, "usedFallback"),
                storagePayloadLimited = ExtractMetadataBool(item.MetadataJson, "storagePayloadLimited"),
                proposalContextLimited = ExtractMetadataBool(item.MetadataJson, "proposalContextLimited"),
                reviewContextLimited = ExtractMetadataBool(item.MetadataJson, "reviewContextLimited"),
                proposalPromptChars = ExtractMetadataInt(item.MetadataJson, "proposalPromptChars"),
                reviewPromptChars = ExtractMetadataInt(item.MetadataJson, "reviewPromptChars"),
                criteriaJson = FormatJson(jobCandidates ?? item.MetadataJson),
                fallbackReason = ExtractFallbackReason(item.MetadataJson),
                selectionMode = ExtractSelectionMode(item.MetadataJson),
                allocationMode = ExtractMetadataString(item.MetadataJson, "allocationMode"),
                selectedScenarioKey = ExtractMetadataString(item.MetadataJson, "selectedScenarioKey"),
                primaryFactors = ExtractMetadataStrings(item.MetadataJson, "primaryFactors"),
                confidence = ExtractMetadataString(item.MetadataJson, "confidence"),
                investorPolicyId = ExtractMetadataString(item.MetadataJson, "investorPolicyId"),
                investorPolicyVersion = ExtractMetadataString(item.MetadataJson, "investorPolicyVersion")
            },
            item.IntelligenceAvailability,
            reasoning = ContributionText.NormalizeCandidateKeys(ContributionText.NormalizeAssetClassNames(item.Reasoning), workItem?.Candidates ?? Array.Empty<ContributionCandidate>()),
            jobStatus = ContributionGuidanceJobStatus.Completed.ToString(),
            reused = true
        };
    }

    private static object PendingResponse(Guid guidanceId, decimal amount, ContributionGuidanceJobStatus jobStatus, bool reused) => new
    {
        guidanceId,
        status = jobStatus == ContributionGuidanceJobStatus.Failed ? "Failed" : "Analyzing",
        contributionAmount = amount,
        requestedAmount = amount,
        allocatedAmount = 0m,
        unallocatedAmount = amount,
        allocationStyle = "Pending",
        allocations = Array.Empty<object>(),
        avoidedCandidates = Array.Empty<object>(),
        reasons = jobStatus == ContributionGuidanceJobStatus.Failed ? new[] { "Não foi possível concluir a análise. Tente gerar uma nova orientação." } : Array.Empty<string>(),
        evidence = Array.Empty<string>(),
        intelligenceAvailability = "Pending",
        jobStatus = jobStatus.ToString(),
        reused
    };

    private static string[] DeserializeEvidence(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    private static string FormatJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "{}";
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static string? DecisionReason(IntegratedDecisionEvidence? evidence) => evidence?.DecisiveFactor switch
    {
        "PrimaryStructuralImprovement" => "Este cenário corrige melhor a principal defasagem da carteira.",
        "Concentration" or "LowerResultingConcentration" => "Entre alternativas equivalentes, esta reduz a concentração da carteira.",
        "SecondaryExposure" => "Entre alternativas equivalentes, esta melhora a diversificação da carteira.",
        "FinancialQuality" => "Entre alternativas estruturalmente equivalentes, esta apresentou melhor qualidade financeira.",
        "RecentContribution" => "Entre alternativas equivalentes, esta evita repetir um aporte recente.",
        "MaintenanceMarginalBenefit" => "A carteira está dentro da tolerância entre classes; este destino apresentou o melhor benefício marginal entre as alternativas elegíveis.",
        "MaintenanceDiversification" => "A carteira está dentro da tolerância entre classes; a divisão reduziu a concentração individual entre alternativas praticamente equivalentes.",
        "StableTieBreaker" => "As alternativas avaliadas ficaram equivalentes pelos critérios disponíveis.",
        _ => null
    };

    private sealed record PersistedDecision(string DecisionType, PersistedNeed? PrimaryNeed, IntegratedDecisionEvidence? DecisionEvidence, string? RebalanceMode);
    private sealed record PersistedNeed(string AssetClass, decimal CurrentPercentage, decimal TargetPercentage, decimal GapPercentagePoints);

    private static PersistedDecision? DeserializeDecision(string json)
    {
        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            if (!TryGet(root, "decisionType", "DecisionType", out var type)) return null;
            PersistedNeed? need = null;
            if (TryGet(root, "primaryNeed", "PrimaryNeed", out var value) && value.ValueKind != JsonValueKind.Null)
                need = JsonSerializer.Deserialize<PersistedNeed>(value.GetRawText(), PersistedJson);
            IntegratedDecisionEvidence? evidence = null;
            if (TryGet(root, "decisionEvidence", "DecisionEvidence", out value) && value.ValueKind != JsonValueKind.Null)
                evidence = JsonSerializer.Deserialize<IntegratedDecisionEvidence>(value.GetRawText(), PersistedJson);
            var rebalanceMode = TryGet(root, "rebalanceMode", "RebalanceMode", out value) ? value.GetString() : null;
            return new(type.GetString() ?? "NoAction", need, evidence, rebalanceMode);
        }
        catch (JsonException) { return null; }
    }

    private static ContributionClassAllocation[] ExtractClassAllocations(string json)
    {
        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            if (!TryGet(root, "classAllocations", "ClassAllocations", out var value) || value.ValueKind != JsonValueKind.Array) return Array.Empty<ContributionClassAllocation>();
            return JsonSerializer.Deserialize<ContributionClassAllocation[]>(value.GetRawText()) ?? Array.Empty<ContributionClassAllocation>();
        }
        catch (JsonException) { return Array.Empty<ContributionClassAllocation>(); }
    }

    private static string? ExtractPolicyVersion(string json)
    {
        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            return TryGet(root, "policyVersion", "PolicyVersion", out var value) ? value.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static string[] ExtractAssessmentVersions(string json)
    {
        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            if (!TryGet(root, "assessmentVersions", "AssessmentVersions", out var value) || value.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            return value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray();
        }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    private static ContributionGuidanceWorkItem? DeserializeWorkItemForRead(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("positions", out _))
            {
                var projected = JsonSerializer.Deserialize<PersistedContributionGuidanceWorkItem>(json);
                return projected?.Baseline is null ? null : projected.ToRuntime();
            }
            return JsonSerializer.Deserialize<ContributionGuidanceWorkItem>(json);
        }
        catch (JsonException) { return null; }
    }

    private static int ExtractMetadataInt(string json, string propertyName)
    {
        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            return root.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var count) ? Math.Max(0, count) : 0;
        }
        catch (JsonException) { return 0; }
    }

    private static int? ExtractMetadataNullableInt(string json, string propertyName)
    {
        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null) return null;
            return value.TryGetInt32(out var count) ? Math.Max(0, count) : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool ExtractMetadataBool(string json, string propertyName)
    {
        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { return false; }
    }

    private static decimal ExtractMetadataDecimal(string json, string propertyName)
    {
        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            return root.TryGetProperty(propertyName, out var value) && value.TryGetDecimal(out var number) ? Math.Max(0, number) : 0;
        }
        catch (JsonException) { return 0; }
    }

    private static int ExtractEvidenceCount(string json, int allocationEvidenceCount)
    {
        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            if (root.TryGetProperty("evidenceCount", out var value) && value.TryGetInt32(out var count)) return Math.Max(0, count);
        }
        catch (JsonException) { }
        return allocationEvidenceCount;
    }

    private static string? ExtractFallbackReason(string json)
    {
        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            if (!TryGet(root, "fallbackReason", "FallbackReason", out var value) || value.ValueKind != JsonValueKind.String)
            {
                var mode = ExtractSelectionMode(json);
                var scenario = ExtractMetadataString(json, "selectedScenarioKey");
                return mode == "single-safe-scenario" ? "Havia apenas um cenário seguro pré-validado; ele foi aplicado diretamente pelo motor de regras."
                    : scenario == "scenario:no-action" ? "A IA analisou os candidatos e concluiu que nenhum aporte em ativo atende aos limites atuais com segurança."
                    : scenario == "scenario:baseline-partial" ? "A IA analisou a distribuição parcial segura; o valor restante permaneceu sem alocação porque não havia outra alternativa elegível."
                    : null;
            }
            return value.GetString() switch
            {
                "deterministic-configured" => "A orientação foi calculada pelo motor de regras; a análise de aportes por IA está desativada na configuração.",
                "no-eligible-candidates" => "Nenhum candidato atende aos limites atuais da carteira.",
                "ai-quota-exceeded" => "O limite de cota da IA foi atingido.",
                "ai-timeout" => "A IA não respondeu dentro do prazo.",
                "ai-invalid-scenario" => "A IA respondeu, mas o cenário não passou nas regras de segurança do Prumo.",
                "ai-invalid-allocation" => "A IA respondeu, mas a distribuição não passou nas regras de segurança do Prumo.",
                "ai-unavailable" => "A IA não respondeu corretamente.",
                "deterministic-partial-allocation" => "A IA foi consultada, mas não havia um cenário completo que respeitasse os limites das classes; foi usada a distribuição parcial segura.",
                _ => null
            };
        }
        catch (JsonException) { return null; }
    }

    private static string? ExtractSelectionMode(string json)
    {
        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            return TryGet(root, "selectionMode", "SelectionMode", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static string? ExtractMetadataString(string json, string property)
    {
        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            return TryGet(root, property, property[..1].ToUpperInvariant() + property[1..], out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static string[] ExtractMetadataStrings(string json, string property)
    {
        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            return TryGet(root, property, property[..1].ToUpperInvariant() + property[1..], out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray()
                : Array.Empty<string>();
        }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    private static bool TryGet(JsonElement root, string camelName, string pascalName, out JsonElement value)
    {
        value = default;
        return root.ValueKind == JsonValueKind.Object
            && (root.TryGetProperty(camelName, out value) || root.TryGetProperty(pascalName, out value));
    }
}

public sealed record ContributionRequest(decimal Amount);
