using Prumo.Application.Explainability;
using Prumo.Application.Abstractions;
using Prumo.Domain.Enums;

namespace Prumo.Application.Contributions;

public enum ContributionIntelligenceAvailability { IntelligenceAvailable, PartialIntelligence, InsufficientIntelligence }

public sealed record InvestorPolicy(
    string PolicyId,
    string PolicyVersion,
    string Strategy,
    string InvestmentHorizon,
    string RebalanceMode,
    bool AllowSellingForRebalance,
    string NewPositionBuildMode,
    bool PreferExistingPositionsWhenEquivalent)
{
    public static InvestorPolicy Current { get; } = new(
        "long-term-income", "v1", "long_term_income", "10y_plus",
        "contributions_only", false, "gradual", true);
}

internal static class ContributionRecency
{
    public static decimal Penalty(decimal configuredPenalty, int recentContributionCount, DateTimeOffset? lastContributionAt, bool isEstablishingPosition = false, decimal currentWeight = 100)
    {
        var basePenalty = configuredPenalty > 0 ? configuredPenalty : recentContributionCount > 0 ? 25m : 0m;
        if (basePenalty <= 0 || lastContributionAt is null) return basePenalty;

        var days = Math.Max(0, (DateTimeOffset.UtcNow - lastContributionAt.Value).TotalDays);
        var factor = days <= 30 ? 1m : days <= 90 ? .67m : days <= 180 ? .33m : 0m;
        var adjustedFactor = isEstablishingPosition && currentWeight < 2m ? factor * .25m : factor;
        return decimal.Round(basePenalty * adjustedFactor, 2);
    }
}

public sealed record ContributionCandidate(
    CandidateIdentity Identity,
    string Ticker,
    AssetClass AssetClass,
    decimal CurrentValue,
    decimal CurrentWeight,
    decimal CurrentClassWeight,
    decimal TargetClassWeight,
    decimal ClassBudget,
    bool HasBlock,
    IReadOnlyList<ContributionAssessmentContext> CurrentAssessments,
    AssetType AssetType = AssetType.Other,
    string? Sector = null,
    decimal RecentContributionAmount = 0,
    int RecentContributionCount = 0,
    DateTimeOffset? LastContributionAt = null,
    decimal SectorWeightWithinClass = 100,
    bool HasPosition = true,
    decimal AllocationNeedScore = 0,
    decimal DiversificationScore = 0,
    decimal UnderweightScore = 0,
    decimal RecentContributionPenalty = 0,
    EconomicSector? SectorCategory = null,
    ExposureCoverage GeographicExposureCoverage = ExposureCoverage.Unavailable,
    IReadOnlyList<KnownGeographicExposure>? GeographicExposures = null,
    ExposureCoverage EconomicCurrencyExposureCoverage = ExposureCoverage.Unavailable,
    IReadOnlyList<KnownCurrencyExposure>? CurrencyExposures = null,
    string? ListingCountryCode = null,
    string? TradingCurrency = null,
    CandidateFinancialSnapshot? FinancialSnapshot = null)
{
    public bool IsEstablishingPosition { get; init; }
    public CandidateIntelligence? CandidateIntelligence { get; init; }
    public string? ExternalName { get; init; }
    public string? ExternalSourceReference { get; init; }
    public Guid AssetId => Identity.AssetId ?? throw new InvalidOperationException("Este candidato não possui AssetId persistido.");
    public CandidateKey CandidateKey => Identity.Key;
    public FundType FundType { get; init; } = FundType.Unknown;
    private UnderlyingSectorExposure? _underlyingSectorExposure;
    public UnderlyingSectorExposure UnderlyingSectorExposure
    {
        get => _underlyingSectorExposure ?? (SectorExposureSemantics.IsFundOfFunds(FundType, Sector) || string.IsNullOrWhiteSpace(Sector)
            ? UnderlyingSectorExposure.Unavailable : new(UnderlyingSectorExposureStatus.Available));
        init => _underlyingSectorExposure = value;
    }
    public bool IsSectorDiversificationKnown => !SectorExposureSemantics.IsFundOfFunds(FundType, Sector) && !string.IsNullOrWhiteSpace(Sector);
    public decimal EffectiveDiversificationScore => DiversificationScore > 0 ? DiversificationScore : IsSectorDiversificationKnown ? Math.Clamp(100m - SectorWeightWithinClass, 0, 100) : 0;
    public decimal DecisionScore => AllocationNeedScore * 0.25m
        + EffectiveDiversificationScore * 0.20m
        + UnderweightScore * 0.15m
        + (100m - ContributionRecency.Penalty(RecentContributionPenalty, RecentContributionCount, LastContributionAt, IsEstablishingPosition, CurrentWeight)) * 0.15m;
}
public sealed record KnownGeographicExposure(string CountryCode, decimal? Percentage, string? Source = null, string? SourceReference = null, DateTimeOffset? ObservedAt = null);
public sealed record KnownCurrencyExposure(string CurrencyCode, decimal? Percentage, string? Source = null, string? SourceReference = null, DateTimeOffset? ObservedAt = null);
public sealed record ContributionAssessmentContext(string AttentionPriority, string ActionRequirement, string CapitalDecisionImpact, string Summary, string WhyItMatters, string SuggestedBehavior, decimal Confidence, IReadOnlyList<EvidenceReference> EvidenceReferences);
public sealed record ContributionCandidateEvaluation(CandidateKey CandidateKey, int Rank, decimal SuggestedAmount, string Reason, decimal Confidence, IReadOnlyList<EvidenceReference> Evidence)
{
    public Guid? AssetId => CandidateKey.Value.StartsWith("KnownCatalog:", StringComparison.Ordinal) && Guid.TryParse(CandidateKey.Value["KnownCatalog:".Length..], out var id) ? id : null;
    public ContributionCandidateEvaluation(Guid assetId, int rank, decimal amount, string reason, decimal confidence, IReadOnlyList<EvidenceReference> evidence)
        : this(CandidateIdentity.Known(assetId).Key, rank, amount, reason, confidence, evidence) { }
}
public sealed record ContributionIntelligenceMetadata(string EngineVersion, string PolicyVersion, string PromptVersion, string Provider, string Model, DateTimeOffset GeneratedAt)
{
    public string? ConfiguredModel { get; init; }
    public string? FallbackModel { get; init; }
    public bool UsedFallback { get; init; }
}
public sealed record ContributionIntelligenceResult(IReadOnlyList<ContributionCandidateEvaluation> CandidateEvaluations, string OverallReasoning, ContributionIntelligenceAvailability Availability, ContributionIntelligenceMetadata Metadata);
public sealed record ContributionScenarioOption(
    string ScenarioKey,
    IReadOnlyList<CandidateAllocation> Allocations,
    decimal Improvement,
    string Summary,
    string? PrimaryNeed = null,
    IReadOnlyList<string>? StructuralReasons = null,
    decimal DeviationBefore = 0,
    decimal DeviationAfter = 0);
public sealed record ContributionScenarioSelection(string SelectedScenarioKey, string Reason, IReadOnlyList<string> Considerations, ContributionIntelligenceAvailability Availability, ContributionIntelligenceMetadata Metadata);
public sealed record ContributionGuidanceWorkItem(
    ContributionGuidance Baseline,
    IReadOnlyList<GuidancePosition> Positions,
    IReadOnlyList<ContributionCandidate> Candidates,
    bool CandidateIntelligencePayloadLimited = false,
    int CandidatePayloadOriginalChars = 0,
    int CandidatePayloadProjectedChars = 0,
    int CandidatePayloadPersistedChars = 0,
    decimal CandidatePayloadReductionPercent = 0,
    int CandidatePayloadTruncatedEventCount = 0,
    int CandidatePayloadTruncatedSummaryCount = 0);

// Persisted contribution contract. It deliberately contains only the data
// needed to rebuild the runtime contribution context; rich application/domain
// records are not serialized directly.
public sealed record PersistedContributionGuidanceWorkItem(
    PersistedContributionBaseline Baseline,
    IReadOnlyList<PersistedContributionCandidate> Candidates,
    bool CandidateIntelligencePayloadLimited = false,
    int CandidatePayloadOriginalChars = 0,
    int CandidatePayloadProjectedChars = 0,
    int CandidatePayloadPersistedChars = 0,
    decimal CandidatePayloadReductionPercent = 0,
    int CandidatePayloadTruncatedEventCount = 0,
    int CandidatePayloadTruncatedSummaryCount = 0)
{
    public ContributionGuidanceWorkItem ToRuntime()
        => new(Baseline.ToRuntime(), Array.Empty<GuidancePosition>(), (Candidates ?? Array.Empty<PersistedContributionCandidate>()).Select(x => x.ToRuntime()).ToArray(), CandidateIntelligencePayloadLimited, CandidatePayloadOriginalChars, CandidatePayloadProjectedChars, CandidatePayloadPersistedChars, CandidatePayloadReductionPercent, CandidatePayloadTruncatedEventCount, CandidatePayloadTruncatedSummaryCount);
}

public sealed record PersistedContributionBaseline(
    ContributionGuidanceStatus Status,
    Guid PortfolioId,
    decimal ContributionAmount,
    IReadOnlyList<PersistedContributionAllocation> Allocations,
    IReadOnlyList<AvoidedCandidate> AvoidedCandidates,
    IReadOnlyList<ContributionClassAllocation> ClassAllocations,
    decimal UnallocatedAmount,
    ContributionAllocationStyle AllocationStyle,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Evidence,
    string PortfolioHealthPolicyVersion,
    IReadOnlyList<string> AssessmentVersions,
    DateTimeOffset GeneratedAt)
{
    public static PersistedContributionBaseline From(ContributionGuidance baseline) => new(
        baseline.Status, baseline.PortfolioId, baseline.ContributionAmount,
        baseline.Allocations.Select(PersistedContributionAllocation.From).ToArray(), baseline.AvoidedCandidates,
        baseline.ClassAllocations, baseline.UnallocatedAmount, baseline.AllocationStyle, baseline.Reasons,
        baseline.Evidence, baseline.PortfolioHealthPolicyVersion, baseline.AssessmentVersions, baseline.GeneratedAt);

    public ContributionGuidance ToRuntime()
    {
        var guidance = new ContributionGuidance(
            Status, PortfolioId, ContributionAmount,
            Allocations.Select(x => x.ToRuntime()).ToArray(), AvoidedCandidates, UnallocatedAmount,
            AllocationStyle, Reasons, Evidence, PortfolioHealthPolicyVersion, AssessmentVersions, GeneratedAt)
        { ClassAllocations = ClassAllocations };
        return guidance;
    }
}

public sealed record PersistedContributionAllocation(
    Guid? AssetId, CandidateIdentity Identity, string CandidateKey, string Ticker, AssetClass AssetClass, decimal Amount, string Reason, IReadOnlyList<string> Evidence)
{
    public static PersistedContributionAllocation From(ContributionAllocation allocation) => new(
        allocation.AssetId, allocation.Identity, allocation.Identity.Key.Value, allocation.Ticker, allocation.AssetClass, allocation.Amount, allocation.Reason, allocation.Evidence);

    public ContributionAllocation ToRuntime()
        => new(AssetId, Ticker, AssetClass, Amount, Reason, Evidence, Identity);
}

public sealed record PersistedContributionCandidate(
    CandidateIdentity Identity,
    string Ticker,
    AssetClass AssetClass,
    decimal CurrentValue,
    decimal CurrentWeight,
    decimal CurrentClassWeight,
    decimal TargetClassWeight,
    decimal ClassBudget,
    bool HasBlock,
    AssetType AssetType,
    string? Sector,
    decimal RecentContributionAmount,
    int RecentContributionCount,
    DateTimeOffset? LastContributionAt,
    decimal SectorWeightWithinClass,
    bool HasPosition,
    decimal AllocationNeedScore,
    decimal DiversificationScore,
    decimal UnderweightScore,
    decimal RecentContributionPenalty,
    EconomicSector? SectorCategory,
    ExposureCoverage GeographicExposureCoverage,
    IReadOnlyList<KnownGeographicExposure>? GeographicExposures,
    ExposureCoverage EconomicCurrencyExposureCoverage,
    IReadOnlyList<KnownCurrencyExposure>? CurrencyExposures,
    string? ListingCountryCode,
    string? TradingCurrency,
    CandidateFinancialSnapshot? FinancialSnapshot,
    bool IsEstablishingPosition,
    FundType FundType,
    UnderlyingSectorExposure UnderlyingSectorExposure,
    CandidateIntelligence? CandidateIntelligence,
    string? ExternalName,
    string? ExternalSourceReference)
{
    public static PersistedContributionCandidate From(ContributionCandidate candidate) => new(
        candidate.Identity, candidate.Ticker, candidate.AssetClass, candidate.CurrentValue, candidate.CurrentWeight,
        candidate.CurrentClassWeight, candidate.TargetClassWeight, candidate.ClassBudget, candidate.HasBlock,
        candidate.AssetType, candidate.Sector, candidate.RecentContributionAmount, candidate.RecentContributionCount,
        candidate.LastContributionAt, candidate.SectorWeightWithinClass, candidate.HasPosition, candidate.AllocationNeedScore,
        candidate.DiversificationScore, candidate.UnderweightScore, candidate.RecentContributionPenalty, candidate.SectorCategory,
        candidate.GeographicExposureCoverage, candidate.GeographicExposures, candidate.EconomicCurrencyExposureCoverage,
        candidate.CurrencyExposures, candidate.ListingCountryCode, candidate.TradingCurrency, candidate.FinancialSnapshot,
        candidate.IsEstablishingPosition, candidate.FundType, candidate.UnderlyingSectorExposure,
        candidate.CandidateIntelligence, candidate.ExternalName, candidate.ExternalSourceReference);

    public ContributionCandidate ToRuntime()
        => new(Identity, Ticker, AssetClass, CurrentValue, CurrentWeight, CurrentClassWeight, TargetClassWeight, ClassBudget,
            HasBlock, Array.Empty<ContributionAssessmentContext>(), AssetType, Sector, RecentContributionAmount,
            RecentContributionCount, LastContributionAt, SectorWeightWithinClass, HasPosition, AllocationNeedScore,
            DiversificationScore, UnderweightScore, RecentContributionPenalty, SectorCategory, GeographicExposureCoverage,
            GeographicExposures, EconomicCurrencyExposureCoverage, CurrencyExposures, ListingCountryCode, TradingCurrency,
            FinancialSnapshot)
        {
            IsEstablishingPosition = IsEstablishingPosition, FundType = FundType,
            UnderlyingSectorExposure = UnderlyingSectorExposure, CandidateIntelligence = CandidateIntelligence,
            ExternalName = ExternalName, ExternalSourceReference = ExternalSourceReference
        };
}

/// <summary>
/// A constrained allocation proposed by the intelligence provider. Candidate keys are
/// deliberately the only identifiers accepted here: the model can distribute capital,
/// but it cannot create an instrument outside of the portfolio's known universe.
/// </summary>
public sealed record AllocationProposalAllocation(CandidateKey CandidateKey, decimal Amount);
public sealed record AllocationProposal(
    string DecisionType,
    IReadOnlyList<AllocationProposalAllocation> Allocations,
    string Reason,
    IReadOnlyList<string> PrimaryFactors,
    string Confidence,
    ContributionIntelligenceAvailability Availability,
    ContributionIntelligenceMetadata Metadata);

public static class AllocationProposalValidator
{
    public static IReadOnlyList<CandidateAllocation> Validate(AllocationProposal proposal, decimal contributionAmount, IReadOnlyList<ContributionCandidate> candidates)
    {
        if (proposal is null) throw new InvalidOperationException("A IA não retornou uma proposta de alocação.");
        var noAction = string.Equals(proposal.DecisionType, "NoAction", StringComparison.OrdinalIgnoreCase);
        if (!noAction && !string.Equals(proposal.DecisionType, "Allocate", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A IA retornou um tipo de decisão inválido.");
        if (noAction && proposal.Allocations.Count > 0)
            throw new InvalidOperationException("A IA retornou alocações para uma decisão sem ação.");
        if (!noAction && proposal.Allocations.Count == 0)
            throw new InvalidOperationException("A IA não retornou nenhuma alocação para a decisão de aporte.");
        if (proposal.Confidence is not ("low" or "medium" or "high"))
            throw new InvalidOperationException("A IA retornou uma confiança inválida.");
        var allowedFactors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "concentration", "diversification", "class_gap", "recent_contribution", "assessment", "exposure", "new_position"
        };
        if (proposal.PrimaryFactors.Any(factor => !allowedFactors.Contains(factor)))
            throw new InvalidOperationException("A IA retornou um fator de decisão inválido.");

        var byKey = candidates.ToDictionary(x => x.CandidateKey);
        if (proposal.Allocations.Any(x => !byKey.ContainsKey(x.CandidateKey) || byKey[x.CandidateKey].HasBlock))
            throw new InvalidOperationException("A IA retornou um candidato inexistente ou bloqueado.");
        if (proposal.Allocations.Any(x => x.Amount <= 0 || decimal.Round(x.Amount, 2) != x.Amount))
            throw new InvalidOperationException("A IA retornou um valor de alocação inválido.");
        if (proposal.Allocations.GroupBy(x => x.CandidateKey).Any(x => x.Count() > 1))
            throw new InvalidOperationException("A IA retornou o mesmo candidato mais de uma vez.");
        if (decimal.Round(proposal.Allocations.Sum(x => x.Amount), 2) > decimal.Round(contributionAmount, 2))
            throw new InvalidOperationException("A IA retornou uma alocação acima do valor do aporte.");
        if (proposal.Allocations.GroupBy(x => byKey[x.CandidateKey].AssetClass)
            .Any(group => group.Sum(x => x.Amount) > byKey[group.First().CandidateKey].ClassBudget))
            throw new InvalidOperationException("A IA retornou uma alocação acima do orçamento de uma classe de ativos.");

        return proposal.Allocations
            .Select(x => new CandidateAllocation(byKey[x.CandidateKey].Identity, x.Amount))
            .ToArray();
    }
}

public static class ContributionScenarioValidator
{
    public static ContributionScenarioOption Select(ContributionScenarioSelection selection, IReadOnlyList<ContributionScenarioOption> scenarios, decimal contributionAmount, IReadOnlyList<ContributionCandidate> candidates)
    {
        var scenario = scenarios.SingleOrDefault(x => x.ScenarioKey == selection.SelectedScenarioKey)
            ?? throw new InvalidOperationException($"A IA selecionou um cenário fora do conjunto seguro: '{selection.SelectedScenarioKey}'.");
        var byKey = candidates.ToDictionary(x => x.CandidateKey);
        if (scenario.Allocations.Count == 0 && scenario.ScenarioKey != "scenario:no-action")
            throw new InvalidOperationException("A IA retornou um cenário sem alocações.");
        if (scenario.Allocations.Any(x => !byKey.TryGetValue(x.Identity.Key, out var candidate) || candidate.HasBlock))
            throw new InvalidOperationException("A IA retornou um cenário com candidato inexistente ou bloqueado.");
        if (scenario.Allocations.Any(x => x.Amount < 0 || decimal.Round(x.Amount, 2) != x.Amount))
            throw new InvalidOperationException("A IA retornou um cenário com valor de alocação inválido.");
        if (scenario.Allocations.Select(x => x.Identity.Key).Distinct().Count() != scenario.Allocations.Count)
            throw new InvalidOperationException("A IA retornou um cenário com o mesmo candidato mais de uma vez.");
        if (scenario.Allocations.GroupBy(x => byKey[x.Identity.Key].AssetClass).Any(group => group.Sum(x => x.Amount) > byKey[group.First().Identity.Key].ClassBudget))
            throw new InvalidOperationException("A IA retornou um cenário acima do orçamento de uma classe de ativos.");
        if (decimal.Round(scenario.Allocations.Sum(x => x.Amount), 2) > decimal.Round(contributionAmount, 2))
            throw new InvalidOperationException("A IA retornou um cenário cuja soma não corresponde ao valor do aporte.");
        return scenario;
    }
}

public interface IContributionIntelligenceProvider
{
    Task<ContributionIntelligenceResult> EvaluateAsync(Guid portfolioId, decimal contributionAmount, IReadOnlyList<ContributionCandidate> candidates, CancellationToken cancellationToken = default);
    Task<ContributionScenarioSelection> SelectScenarioAsync(Guid portfolioId, decimal contributionAmount, IReadOnlyList<ContributionScenarioOption> scenarios, IReadOnlyList<ContributionCandidate> candidates, InvestorPolicy? investorPolicy = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Este provider não oferece seleção de cenários.");
    Task<AllocationProposal> ProposeAllocationAsync(Guid portfolioId, decimal contributionAmount, IReadOnlyList<ContributionCandidate> candidates, IReadOnlyList<ContributionScenarioOption> deterministicReferences, InvestorPolicy? investorPolicy = null, string? rejectionReason = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Este provider não oferece geração de alocação.");
    Task<AllocationReview> ReviewAllocationAsync(Guid portfolioId, decimal contributionAmount, IReadOnlyList<ContributionCandidate> candidates, IReadOnlyList<ContributionScenarioOption> deterministicReferences, InvestorPolicy investorPolicy, CompositionProjection projection, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Este provider não oferece revisão de composição.");
}

public static class ContributionIntelligenceValidator
{
    public static bool IsValid(ContributionIntelligenceResult result, decimal contributionAmount, IReadOnlyList<ContributionCandidate> candidates)
    {
        if (result is null || result.CandidateEvaluations.Any(x => x.Rank <= 0 || x.SuggestedAmount < 0 || x.Confidence is < 0 or > 1)
            || result.CandidateEvaluations.GroupBy(x => x.CandidateKey).Any(x => x.Count() > 1)
            || result.CandidateEvaluations.GroupBy(x => x.Rank).Any(x => x.Count() > 1)) return false;
        var byKey = candidates.ToDictionary(x => x.CandidateKey);
        if (result.CandidateEvaluations.Any(x => !byKey.TryGetValue(x.CandidateKey, out var candidate) || candidate.HasBlock || Money(x.SuggestedAmount) > candidate.ClassBudget)) return false;
        if (result.CandidateEvaluations
            .GroupBy(x => byKey[x.CandidateKey].AssetClass)
            .Any(group => group.Sum(x => Money(x.SuggestedAmount)) > candidates.First(x => x.AssetClass == group.Key).ClassBudget)) return false;
        return result.CandidateEvaluations.Sum(x => Money(x.SuggestedAmount)) <= Money(contributionAmount);
    }

    private static decimal Money(decimal value) => decimal.Round(value, 2);
}

public static class ContributionIntelligenceApplier
{
    public static ContributionGuidance Apply(ContributionGuidance fallback, ContributionIntelligenceResult result, IReadOnlyList<GuidancePosition> positions)
        => Apply(fallback, result, positions.Select(x => new ContributionCandidate(CandidateIdentity.Known(x.AssetId), x.Ticker, x.AssetClass, x.CurrentValue, x.CurrentWeight, 0, 0, 0, false, Array.Empty<ContributionAssessmentContext>(), x.AssetType)).ToArray(), positions);

    public static ContributionGuidance Apply(ContributionGuidance fallback, ContributionIntelligenceResult result, IReadOnlyList<ContributionCandidate> candidates, IReadOnlyList<GuidancePosition> positions)
    {
        var byKey = candidates.ToDictionary(x => x.CandidateKey);
        var allocations = result.CandidateEvaluations.Where(x => x.SuggestedAmount > 0 && byKey.ContainsKey(x.CandidateKey)).OrderBy(x => x.Rank).Select(x =>
        {
            var candidate = byKey[x.CandidateKey];
            return new ContributionAllocation(candidate.Identity.AssetId, candidate.Ticker, candidate.AssetClass, decimal.Round(x.SuggestedAmount, 2), ContributionText.NormalizeAssetClassNames(x.Reason), x.Evidence.Select(e => e.Label).Where(e => !string.IsNullOrWhiteSpace(e)).ToArray(), candidate.Identity);
        }).ToArray();
        var reasons = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.OverallReasoning)) reasons.Add(ContributionText.NormalizeAssetClassNames(result.OverallReasoning));
        var deferred = candidatesForExplanation(positions, allocations.Where(x => x.AssetId is not null).Select(x => x.AssetId!.Value).ToHashSet());
        if (deferred.Count > 0) reasons.Add($"Alternativas adiadas: {string.Join(" ", deferred)}");
        return fallback with { Allocations = allocations, UnallocatedAmount = decimal.Round(fallback.ContributionAmount - allocations.Sum(x => x.Amount), 2), Reasons = reasons, Evidence = allocations.SelectMany(x => x.Evidence).Distinct().ToArray(), AllocationStyle = allocations.Length switch { 0 => ContributionAllocationStyle.NoAllocation, 1 => ContributionAllocationStyle.Concentrated, _ => ContributionAllocationStyle.Distributed } };
    }

    public static ContributionGuidance ApplyFallback(ContributionGuidance fallback, string reason)
    {
        var reasons = string.IsNullOrWhiteSpace(reason) ? fallback.Reasons.ToList() : new[] { reason }.ToList();
        var deferred = fallback.AvoidedCandidates.Where(x => x.ReasonType == ContributionAvoidanceReasonType.LowerPriority)
            .Select(x => $"{x.Ticker}: {x.Reason}").ToArray();
        if (deferred.Length > 0) reasons.Add($"Alternativas adiadas: {string.Join(" ", deferred)}");
        return fallback with { Reasons = reasons };
    }

    private static List<string> candidatesForExplanation(IReadOnlyList<GuidancePosition> positions, IReadOnlySet<Guid> selectedIds)
        => positions.Where(x => !selectedIds.Contains(x.AssetId) && x.RecentContributionCount > 0)
            .OrderByDescending(x => x.RecentContributionAmount)
            .Take(3)
            .Select(x => $"{x.Ticker} recebeu {x.RecentContributionCount} aporte(s) recentemente e, por isso, ficou com menor prioridade neste cenário.")
            .ToList();
}

public sealed class DeterministicContributionIntelligenceProvider : IContributionIntelligenceProvider
{
    public Task<AllocationProposal> ProposeAllocationAsync(Guid portfolioId, decimal contributionAmount, IReadOnlyList<ContributionCandidate> candidates, IReadOnlyList<ContributionScenarioOption> deterministicReferences, InvestorPolicy? investorPolicy = null, string? rejectionReason = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new AllocationProposal("NoAction", Array.Empty<AllocationProposalAllocation>(), "A IA não está configurada; foi aplicado o fallback determinístico.", Array.Empty<string>(), "low", ContributionIntelligenceAvailability.InsufficientIntelligence, new("R5-ai-allocation-v2", "deterministic", "none", "deterministic", "none", DateTimeOffset.UtcNow)));

    public Task<AllocationReview> ReviewAllocationAsync(Guid portfolioId, decimal contributionAmount, IReadOnlyList<ContributionCandidate> candidates, IReadOnlyList<ContributionScenarioOption> deterministicReferences, InvestorPolicy investorPolicy, CompositionProjection projection, CancellationToken cancellationToken = default)
        => Task.FromResult(new AllocationReview("Confirm", Array.Empty<AllocationProposalAllocation>(), "A revisão por IA não está configurada; a proposta inicial válida foi preservada.", Array.Empty<string>(), ContributionIntelligenceAvailability.InsufficientIntelligence, new("R5-ai-allocation-v2", "deterministic", "none", "deterministic", "none", DateTimeOffset.UtcNow)));

    public Task<ContributionScenarioSelection> SelectScenarioAsync(Guid portfolioId, decimal contributionAmount, IReadOnlyList<ContributionScenarioOption> scenarios, IReadOnlyList<ContributionCandidate> candidates, InvestorPolicy? investorPolicy = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new ContributionScenarioSelection(string.Empty, "A IA não está configurada; foi aplicado o fallback determinístico.", Array.Empty<string>(), ContributionIntelligenceAvailability.InsufficientIntelligence, new("R17.8", "deterministic", "none", "deterministic", "none", DateTimeOffset.UtcNow)));

    public Task<ContributionIntelligenceResult> EvaluateAsync(Guid portfolioId, decimal contributionAmount, IReadOnlyList<ContributionCandidate> candidates, CancellationToken cancellationToken = default)
        => Task.FromResult(new ContributionIntelligenceResult(Array.Empty<ContributionCandidateEvaluation>(), "Não há inteligência suficiente para diferenciar candidatos com segurança; foi aplicado o fallback determinístico.", ContributionIntelligenceAvailability.InsufficientIntelligence, new("R4-2.0", "deterministic", "none", "deterministic", "none", DateTimeOffset.UtcNow)));
}
