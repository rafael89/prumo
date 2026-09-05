using Prumo.Application.Explainability;
using Prumo.Application.Abstractions;
using Prumo.Domain.Enums;

namespace Prumo.Application.Contributions;

public enum ContributionIntelligenceAvailability { IntelligenceAvailable, PartialIntelligence, InsufficientIntelligence }

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
    public string? ExternalName { get; init; }
    public string? ExternalSourceReference { get; init; }
    public Guid AssetId => Identity.AssetId ?? throw new InvalidOperationException("Este candidato não possui AssetId persistido.");
    public CandidateKey CandidateKey => Identity.Key;
    public decimal DecisionScore => AllocationNeedScore * 0.25m
        + (DiversificationScore > 0 ? DiversificationScore : Math.Clamp(100m - SectorWeightWithinClass, 0, 100)) * 0.20m
        + UnderweightScore * 0.15m
        + (100m - (RecentContributionPenalty > 0 ? RecentContributionPenalty : RecentContributionCount > 0 ? 25m : 0m)) * 0.15m;
}
public sealed record KnownGeographicExposure(string CountryCode, decimal? Percentage);
public sealed record KnownCurrencyExposure(string CurrencyCode, decimal? Percentage);
public sealed record ContributionAssessmentContext(string AttentionPriority, string ActionRequirement, string CapitalDecisionImpact, string Summary, string WhyItMatters, string SuggestedBehavior, decimal Confidence, IReadOnlyList<EvidenceReference> EvidenceReferences);
public sealed record ContributionCandidateEvaluation(CandidateKey CandidateKey, int Rank, decimal SuggestedAmount, string Reason, decimal Confidence, IReadOnlyList<EvidenceReference> Evidence)
{
    public Guid? AssetId => CandidateKey.Value.StartsWith("KnownCatalog:", StringComparison.Ordinal) && Guid.TryParse(CandidateKey.Value["KnownCatalog:".Length..], out var id) ? id : null;
    public ContributionCandidateEvaluation(Guid assetId, int rank, decimal amount, string reason, decimal confidence, IReadOnlyList<EvidenceReference> evidence)
        : this(CandidateIdentity.Known(assetId).Key, rank, amount, reason, confidence, evidence) { }
}
public sealed record ContributionIntelligenceMetadata(string EngineVersion, string PolicyVersion, string PromptVersion, string Provider, string Model, DateTimeOffset GeneratedAt);
public sealed record ContributionIntelligenceResult(IReadOnlyList<ContributionCandidateEvaluation> CandidateEvaluations, string OverallReasoning, ContributionIntelligenceAvailability Availability, ContributionIntelligenceMetadata Metadata);
public sealed record ContributionScenarioOption(string ScenarioKey, IReadOnlyList<CandidateAllocation> Allocations, decimal Improvement, string Summary);
public sealed record ContributionScenarioSelection(string SelectedScenarioKey, string Reason, IReadOnlyList<string> Considerations, ContributionIntelligenceAvailability Availability, ContributionIntelligenceMetadata Metadata);
public sealed record ContributionGuidanceWorkItem(ContributionGuidance Baseline, IReadOnlyList<GuidancePosition> Positions, IReadOnlyList<ContributionCandidate> Candidates);

public static class ContributionScenarioValidator
{
    public static ContributionScenarioOption Select(ContributionScenarioSelection selection, IReadOnlyList<ContributionScenarioOption> scenarios, decimal contributionAmount, IReadOnlyList<ContributionCandidate> candidates)
    {
        var scenario = scenarios.SingleOrDefault(x => x.ScenarioKey == selection.SelectedScenarioKey)
            ?? throw new InvalidOperationException($"A IA selecionou um cenário fora do conjunto seguro: '{selection.SelectedScenarioKey}'.");
        var byKey = candidates.ToDictionary(x => x.CandidateKey);
        if (scenario.Allocations.Count == 0)
            throw new InvalidOperationException("A IA retornou um cenário sem alocações.");
        if (scenario.Allocations.Any(x => !byKey.TryGetValue(x.Identity.Key, out var candidate) || candidate.HasBlock))
            throw new InvalidOperationException("A IA retornou um cenário com candidato inexistente ou bloqueado.");
        if (scenario.Allocations.Any(x => x.Amount < 0 || decimal.Round(x.Amount, 2) != x.Amount))
            throw new InvalidOperationException("A IA retornou um cenário com valor de alocação inválido.");
        if (scenario.Allocations.Select(x => x.Identity.Key).Distinct().Count() != scenario.Allocations.Count)
            throw new InvalidOperationException("A IA retornou um cenário com o mesmo candidato mais de uma vez.");
        if (scenario.Allocations.GroupBy(x => byKey[x.Identity.Key].AssetClass).Any(group => group.Sum(x => x.Amount) > byKey[group.First().Identity.Key].ClassBudget))
            throw new InvalidOperationException("A IA retornou um cenário acima do orçamento de uma classe de ativos.");
        if (decimal.Round(scenario.Allocations.Sum(x => x.Amount), 2) != decimal.Round(contributionAmount, 2))
            throw new InvalidOperationException("A IA retornou um cenário cuja soma não corresponde ao valor do aporte.");
        return scenario;
    }
}

public interface IContributionIntelligenceProvider
{
    Task<ContributionIntelligenceResult> EvaluateAsync(Guid portfolioId, decimal contributionAmount, IReadOnlyList<ContributionCandidate> candidates, CancellationToken cancellationToken = default);
    Task<ContributionScenarioSelection> SelectScenarioAsync(Guid portfolioId, decimal contributionAmount, IReadOnlyList<ContributionScenarioOption> scenarios, IReadOnlyList<ContributionCandidate> candidates, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Este provider não oferece seleção de cenários.");
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
    public Task<ContributionScenarioSelection> SelectScenarioAsync(Guid portfolioId, decimal contributionAmount, IReadOnlyList<ContributionScenarioOption> scenarios, IReadOnlyList<ContributionCandidate> candidates, CancellationToken cancellationToken = default)
        => Task.FromResult(new ContributionScenarioSelection(string.Empty, "A IA não está configurada; foi aplicado o fallback determinístico.", Array.Empty<string>(), ContributionIntelligenceAvailability.InsufficientIntelligence, new("R17.8", "deterministic", "none", "deterministic", "none", DateTimeOffset.UtcNow)));

    public Task<ContributionIntelligenceResult> EvaluateAsync(Guid portfolioId, decimal contributionAmount, IReadOnlyList<ContributionCandidate> candidates, CancellationToken cancellationToken = default)
        => Task.FromResult(new ContributionIntelligenceResult(Array.Empty<ContributionCandidateEvaluation>(), "Não há inteligência suficiente para diferenciar candidatos com segurança; foi aplicado o fallback determinístico.", ContributionIntelligenceAvailability.InsufficientIntelligence, new("R4-2.0", "deterministic", "none", "deterministic", "none", DateTimeOffset.UtcNow)));
}
