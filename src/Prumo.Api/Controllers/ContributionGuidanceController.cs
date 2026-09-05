using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prumo.Application.Abstractions;
using Prumo.Application.Contributions;
using Prumo.Application.Explainability;
using Prumo.Application.Portfolio;
using Prumo.Api.Services;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Domain.Rules;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Reconciliation;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/contribution-guidance")]
public sealed class ContributionGuidanceController(PrumoDbContext db, PortfolioAssessmentCurrentService current, TransactionReconciliationService? canonical = null) : ControllerBase
{
    private static readonly JsonSerializerOptions PersistedJson = new() { PropertyNameCaseInsensitive = true };
    private const string ContributionEngineVersion = "R4-9.0";
    private static readonly TimeSpan RecentContributionWindow = TimeSpan.FromDays(90);

    [HttpPost]
    public async Task<ActionResult<object>> Generate(Guid portfolioId, ContributionRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0) return BadRequest("O valor do aporte deve ser positivo.");

        var portfolio = await db.Portfolios.Include(x => x.Positions).ThenInclude(x => x.Asset).ThenInclude(x => x!.GeographicExposures)
            .Include(x => x.Positions).ThenInclude(x => x.Asset).ThenInclude(x => x!.CurrencyExposures)
            .Include(x => x.Positions).ThenInclude(x => x.Asset).ThenInclude(x => x!.FundamentalSnapshots)
            .Include(x => x.Positions).ThenInclude(x => x.Asset).ThenInclude(x => x!.DocumentFinancialFacts)
            .SingleOrDefaultAsync(x => x.Id == portfolioId, ct);
        if (portfolio is null) return NotFound();

        var targets = await db.TargetAllocations.AsNoTracking().Where(x => x.PortfolioId == portfolioId).ToListAsync(ct);
        if (targets.Count == 0) return Conflict(new { status = ContributionGuidanceStatus.NotConfigured.ToString() });
        if (portfolio.Positions.Any(x => !x.CurrentValue.HasValue)) return BadRequest("Não há preço válido para todas as posições existentes.");

        var snapshot = PortfolioSnapshotKeyCalculator.Calculate(portfolio);
        var targetKey = TargetAllocationKeyCalculator.Calculate(targets)!;
        var amount = decimal.Round(request.Amount, 2);
        var recentSince = DateTimeOffset.UtcNow.Subtract(RecentContributionWindow);
        var recentPurchases = (await (canonical ?? new TransactionReconciliationService(db)).Canonical(portfolioId, ct))
            .Where(x => x.Type == nameof(PortfolioTransactionType.Buy) && x.OccurredAt >= recentSince)
            .ToList();
        var recentByAsset = recentPurchases.SelectMany(transaction => transaction.Items.Select(item => new
            {
                item.AssetId,
                Amount = item.Quantity * item.UnitPrice,
                transaction.OccurredAt
            }))
            .GroupBy(x => x.AssetId)
            .ToDictionary(x => x.Key, x => new
            {
                Amount = decimal.Round(x.Sum(item => item.Amount), 2),
                Count = x.Count(),
                LastOccurredAt = x.Max(item => item.OccurredAt)
            });
        var classTotals = portfolio.Positions.Where(x => x.Asset is not null && x.CurrentValue.HasValue)
            .GroupBy(x => x.Asset!.AssetClass)
            .ToDictionary(x => x.Key, x => x.Sum(position => position.CurrentValue!.Value));
        var sectorTotals = portfolio.Positions.Where(x => x.Asset is not null && x.CurrentValue.HasValue)
            .GroupBy(x => new { AssetClass = x.Asset!.AssetClass, Sector = NormalizeSector(x.Asset.Sector) })
            .ToDictionary(x => x.Key, x => x.Sum(position => position.CurrentValue!.Value));

        var health = new PortfolioHealthCalculator().Calculate(portfolio, targets);
        var total = portfolio.Positions.Sum(x => x.CurrentValue ?? 0);
        var existingPositions = portfolio.Positions.Where(x => x.Asset is not null && x.CurrentValue.HasValue)
            .Select(x =>
            {
                var asset = x.Asset!;
                var recent = recentByAsset.GetValueOrDefault(x.AssetId);
                var sector = NormalizeSector(asset.Sector);
                var sectorValue = sector.Length == 0 ? 0 : sectorTotals.GetValueOrDefault(new { AssetClass = asset.AssetClass, Sector = sector });
                var sectorWeight = sector.Length == 0 || classTotals.GetValueOrDefault(asset.AssetClass) <= 0
                    ? 100
                    : sectorValue / classTotals[asset.AssetClass] * 100m;
                var deviation = health.AllocationDeviations.FirstOrDefault(x => x.AssetClass == asset.AssetClass);
                var allocationNeedScore = ScoreNeed(deviation?.TargetPercentage ?? 0, deviation?.CurrentPercentage ?? 0);
                var diversificationScore = sector.Length == 0 ? 0 : Math.Clamp(100m - sectorWeight, 0, 100);
                var underweightScore = Math.Clamp(100m - (total == 0 ? 0 : x.CurrentValue!.Value / total * 100m), 0, 100);
                var recentPenalty = recent is null ? 0 : Math.Clamp(25m + recent.Amount / Math.Max(total, 1m) * 100m, 0, 100);
                return new GuidancePosition(x.AssetId, asset.Ticker, asset.AssetClass, x.CurrentValue!.Value,
                    total == 0 ? 0 : x.CurrentValue.Value / total * 100m, asset.AssetType, asset.Sector,
                    recent?.Amount ?? 0, recent?.Count ?? 0, recent?.LastOccurredAt, sectorWeight, true,
                    allocationNeedScore, diversificationScore, underweightScore, recentPenalty,
                    asset.SectorCategory, asset.GeographicExposureCoverage,
                    asset.GeographicExposures.Select(e => new KnownGeographicExposure(e.CountryCode, e.Percentage)).ToArray(),
                    asset.EconomicCurrencyExposureCoverage,
                    asset.CurrencyExposures.Select(e => new KnownCurrencyExposure(e.CurrencyCode, e.Percentage)).ToArray(),
                    asset.ListingCountryCode, asset.TradingCurrency, DocumentFinancialQualityResolver.Resolve(asset));
            }).ToList();
        var positionAssetIds = existingPositions.Select(x => x.AssetId).ToHashSet();
        var targetClasses = targets.Select(x => x.AssetClass).ToHashSet();
        var catalogCandidates = await db.Assets.AsNoTracking()
            .Include(x => x.FundamentalSnapshots).Include(x => x.DocumentFinancialFacts)
            .Where(x => x.IsActive && (targetClasses.Contains(x.AssetClass) || targetClasses.Contains(AssetClass.International) && x.AssetType == AssetType.Etf) && !positionAssetIds.Contains(x.Id))
            .OrderBy(x => x.Ticker)
            .ToListAsync(ct);
        var positions = existingPositions.Concat(catalogCandidates.Select(asset =>
        {
            var deviation = health.AllocationDeviations.FirstOrDefault(x => x.AssetClass == asset.AssetClass);
            var assetClass = asset.AssetType == AssetType.Etf ? AssetClass.International : asset.AssetClass;
            return new GuidancePosition(asset.Id, asset.Ticker, assetClass, 0, 0, asset.AssetType, asset.Sector,
                HasPosition: false, AllocationNeedScore: ScoreNeed(deviation?.TargetPercentage ?? 0, deviation?.CurrentPercentage ?? 0),
                UnderweightScore: 100, SectorCategory: asset.SectorCategory,
                GeographicExposureCoverage: asset.GeographicExposureCoverage,
                GeographicExposures: asset.GeographicExposures.Select(e => new KnownGeographicExposure(e.CountryCode, e.Percentage)).ToArray(),
                EconomicCurrencyExposureCoverage: asset.EconomicCurrencyExposureCoverage,
                CurrencyExposures: asset.CurrencyExposures.Select(e => new KnownCurrencyExposure(e.CurrencyCode, e.Percentage)).ToArray(),
                ListingCountryCode: asset.ListingCountryCode, TradingCurrency: asset.TradingCurrency, FinancialSnapshot: DocumentFinancialQualityResolver.Resolve(asset));
        })).ToList();
        var ids = positions.Select(x => x.AssetId).ToHashSet();
        var assessments = await db.PortfolioAssessments.AsNoTracking().AsSplitQuery()
            .Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets)
            .Include(x => x.InvestmentEvent!).ThenInclude(x => x.Evidence)
            .Where(x => x.PortfolioId == portfolioId && x.Status != AssessmentStatus.Closed && x.InvestmentEvent!.AffectedAssets.Any(a => ids.Contains(a.Id))).ToListAsync(ct);
        assessments = (await current.EnsureFreshAsync(portfolio, targets, assessments, ct)).ToList();

        var inputs = assessments.Where(x => x.InvestmentEvent is not null)
            .SelectMany(x => x.InvestmentEvent!.AffectedAssets.Where(a => ids.Contains(a.Id)).Select(a => new GuidanceAssessment(
                a.Id, x.CapitalDecisionImpact, x.Status, x.InvestmentEvent.Evidence.FirstOrDefault()?.Reference, x.AssessmentVersion,
                x.AttentionPriority, x.ActionRequirement, x.Summary, x.WhyItMatters, x.SuggestedBehavior, ConfidenceValue(x.Confidence), x.Id))).ToList();
        var intelligenceContextKey = ContributionIntelligenceContextKeyCalculator.Calculate(inputs);
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
            if (existingStatus is ContributionGuidanceJobStatus.Completed)
                return Ok(await CompletedResponseAsync(portfolioId, existing, ct));
            if (existingStatus is ContributionGuidanceJobStatus.Queued or ContributionGuidanceJobStatus.Processing)
                return Accepted(PendingResponse(existing.Id, existing.RequestedAmount, existingStatus.Value, true));
        }

        var baseline = new ContributionGuidanceCalculator().Calculate(new ContributionGuidanceInput(portfolioId, amount, health, positions, inputs));
        var classBudgets = baseline.Allocations.GroupBy(x => x.AssetClass).ToDictionary(x => x.Key, x => x.Sum(a => a.Amount));
        var deviations = health.AllocationDeviations.GroupBy(x => x.AssetClass).ToDictionary(x => x.Key, x => x.First());
        var candidates = positions.Select(position =>
        {
            var contexts = inputs.Where(x => x.AssetId == position.AssetId).Select(x => new ContributionAssessmentContext(
                x.AttentionPriority.ToString(), x.ActionRequirement.ToString(), x.Impact.ToString(), x.Summary, x.WhyItMatters,
                x.SuggestedBehavior, x.Confidence,
                new[] { new EvidenceReference("assessment", $"{x.AssessmentId}:{x.AssessmentVersion}", x.Evidence ?? x.Summary, x.WhyItMatters) })).ToArray();
            var deviation = deviations.GetValueOrDefault(position.AssetClass);
            return new ContributionCandidate(CandidateIdentity.Known(position.AssetId), position.Ticker, position.AssetClass, position.CurrentValue, position.CurrentWeight,
                deviation?.CurrentPercentage ?? 0, deviation?.TargetPercentage ?? 0, classBudgets.GetValueOrDefault(position.AssetClass),
                inputs.Any(x => x.AssetId == position.AssetId && x.Impact == CapitalDecisionImpact.Block), contexts,
                position.AssetType, position.Sector, position.RecentContributionAmount, position.RecentContributionCount,
                position.LastContributionAt, position.SectorWeightWithinClass, position.HasPosition,
                position.AllocationNeedScore, position.DiversificationScore, position.UnderweightScore,
                position.RecentContributionPenalty, position.SectorCategory, position.GeographicExposureCoverage,
                position.GeographicExposures, position.EconomicCurrencyExposureCoverage, position.CurrencyExposures,
                position.ListingCountryCode, position.TradingCurrency, position.FinancialSnapshot);
        }).ToArray();

        var persisted = new PersistedContributionGuidance(portfolioId, amount, snapshot, targetKey, intelligenceContextKey, ContributionEngineVersion, "pending-ai", "pending", "Pending", "Análise em andamento: o Prumo está avaliando os candidatos e os limites da carteira.", "{}");
        db.ContributionGuidances.Add(persisted);
        var job = new ContributionGuidanceJob(portfolioId, persisted.Id, JsonSerializer.Serialize(new ContributionGuidanceWorkItem(baseline, positions, candidates)));
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
        var positions = await db.PortfolioPositions.AsNoTracking().Include(x => x.Asset)
            .Where(x => x.PortfolioId == portfolioId).ToDictionaryAsync(x => x.AssetId, ct);
        var assets = await db.Assets.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var allocations = item.Allocations.Select(x => new
        {
            x.AssetId,
            x.Origin,
            x.CandidateKey,
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
            workItem = JsonSerializer.Deserialize<ContributionGuidanceWorkItem>(jobCandidates);
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
                    reason = ContributionText.NormalizeAssetClassNames(x.Reason)
                })
                .ToArray() ?? Array.Empty<object>();
        }
        var decision = DeserializeDecision(item.MetadataJson);
        var allocated = allocations.Sum(x => x.Amount) + classAllocations.Sum(x => x.Amount);
        return new
        {
            guidanceId = item.Id,
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
                decisionType = decision.DecisionType,
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
                provider = item.Provider,
                model = item.Model,
                policyVersion = ExtractPolicyVersion(item.MetadataJson),
                assessmentVersions = ExtractAssessmentVersions(item.MetadataJson),
                evidenceCount = allocations.SelectMany(x => x.evidence).Distinct().Count()
            },
            item.IntelligenceAvailability,
            reasoning = ContributionText.NormalizeAssetClassNames(item.Reasoning),
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

    private static decimal ConfidenceValue(IntelligenceConfidence confidence) => confidence switch
    {
        IntelligenceConfidence.High => 0.9m,
        IntelligenceConfidence.Medium => 0.7m,
        _ => 0.4m
    };

    private static string NormalizeSector(string? sector) => string.IsNullOrWhiteSpace(sector) ? string.Empty : sector.Trim().ToUpperInvariant();

    private static decimal ScoreNeed(decimal target, decimal current) => Math.Clamp((target - current) * 5m, 0, 100);

    private static string[] DeserializeEvidence(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    private static string? DecisionReason(IntegratedDecisionEvidence? evidence) => evidence?.DecisiveFactor switch
    {
        "PrimaryStructuralImprovement" => "Este cenário corrige melhor a principal defasagem da carteira.",
        "Concentration" or "LowerResultingConcentration" => "Entre alternativas equivalentes, esta reduz a concentração da carteira.",
        "SecondaryExposure" => "Entre alternativas equivalentes, esta melhora a diversificação da carteira.",
        "FinancialQuality" => "Entre alternativas estruturalmente equivalentes, esta apresentou melhor qualidade financeira.",
        "RecentContribution" => "Entre alternativas equivalentes, esta evita repetir um aporte recente.",
        "StableTieBreaker" => "As alternativas avaliadas ficaram equivalentes pelos critérios disponíveis.",
        _ => null
    };

    private sealed record PersistedDecision(string DecisionType, PersistedNeed? PrimaryNeed, IntegratedDecisionEvidence? DecisionEvidence);
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
            return new(type.GetString() ?? "NoAction", need, evidence);
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

    private static bool TryGet(JsonElement root, string camelName, string pascalName, out JsonElement value)
        => root.TryGetProperty(camelName, out value) || root.TryGetProperty(pascalName, out value);
}

public sealed record ContributionRequest(decimal Amount);
