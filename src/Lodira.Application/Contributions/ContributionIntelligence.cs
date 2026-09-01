using Lodira.Application.Explainability;
using Lodira.Domain.Enums;

namespace Lodira.Application.Contributions;

public enum ContributionIntelligenceAvailability { IntelligenceAvailable, PartialIntelligence, InsufficientIntelligence }

public sealed record ContributionCandidate(
    Guid AssetId,
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
    decimal RecentContributionPenalty = 0)
{
    public decimal DecisionScore => AllocationNeedScore * 0.25m
        + (DiversificationScore > 0 ? DiversificationScore : Math.Clamp(100m - SectorWeightWithinClass, 0, 100)) * 0.20m
        + UnderweightScore * 0.15m
        + (100m - (RecentContributionPenalty > 0 ? RecentContributionPenalty : RecentContributionCount > 0 ? 25m : 0m)) * 0.15m;
}
public sealed record ContributionAssessmentContext(string AttentionPriority, string ActionRequirement, string CapitalDecisionImpact, string Summary, string WhyItMatters, string SuggestedBehavior, decimal Confidence, IReadOnlyList<EvidenceReference> EvidenceReferences);
public sealed record ContributionCandidateEvaluation(Guid AssetId, int Rank, decimal SuggestedAmount, string Reason, decimal Confidence, IReadOnlyList<EvidenceReference> Evidence);
public sealed record ContributionIntelligenceMetadata(string EngineVersion, string PolicyVersion, string PromptVersion, string Provider, string Model, DateTimeOffset GeneratedAt);
public sealed record ContributionIntelligenceResult(IReadOnlyList<ContributionCandidateEvaluation> CandidateEvaluations, string OverallReasoning, ContributionIntelligenceAvailability Availability, ContributionIntelligenceMetadata Metadata);
public sealed record ContributionGuidanceWorkItem(ContributionGuidance Baseline, IReadOnlyList<GuidancePosition> Positions, IReadOnlyList<ContributionCandidate> Candidates);

public interface IContributionIntelligenceProvider
{
    Task<ContributionIntelligenceResult> EvaluateAsync(Guid portfolioId, decimal contributionAmount, IReadOnlyList<ContributionCandidate> candidates, CancellationToken cancellationToken = default);
}

public static class ContributionIntelligenceValidator
{
    public static bool IsValid(ContributionIntelligenceResult result, decimal contributionAmount, IReadOnlyList<ContributionCandidate> candidates)
    {
        if (result is null || result.CandidateEvaluations.Any(x => x.Rank <= 0 || x.SuggestedAmount < 0 || x.Confidence is < 0 or > 1)
            || result.CandidateEvaluations.GroupBy(x => x.AssetId).Any(x => x.Count() > 1)
            || result.CandidateEvaluations.GroupBy(x => x.Rank).Any(x => x.Count() > 1)) return false;
        var byId = candidates.ToDictionary(x => x.AssetId);
        if (result.CandidateEvaluations.Any(x => !byId.TryGetValue(x.AssetId, out var candidate) || candidate.HasBlock || Money(x.SuggestedAmount) > candidate.ClassBudget)) return false;
        if (result.CandidateEvaluations
            .GroupBy(x => byId[x.AssetId].AssetClass)
            .Any(group => group.Sum(x => Money(x.SuggestedAmount)) > candidates.First(x => x.AssetClass == group.Key).ClassBudget)) return false;
        return result.CandidateEvaluations.Sum(x => Money(x.SuggestedAmount)) <= Money(contributionAmount);
    }

    private static decimal Money(decimal value) => decimal.Round(value, 2);
}

public static class ContributionIntelligenceApplier
{
    public static ContributionGuidance Apply(ContributionGuidance fallback, ContributionIntelligenceResult result, IReadOnlyList<GuidancePosition> positions)
    {
        var byId = positions.ToDictionary(x => x.AssetId);
        var allocations = result.CandidateEvaluations.Where(x => x.SuggestedAmount > 0 && byId.ContainsKey(x.AssetId)).OrderBy(x => x.Rank).Select(x =>
        {
            var position = byId[x.AssetId];
            return new ContributionAllocation(x.AssetId, position.Ticker, position.AssetClass, decimal.Round(x.SuggestedAmount, 2), x.Reason, x.Evidence.Select(e => e.Label).Where(e => !string.IsNullOrWhiteSpace(e)).ToArray());
        }).ToArray();
        var reasons = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.OverallReasoning)) reasons.Add(result.OverallReasoning);
        var deferred = candidatesForExplanation(positions, allocations.Select(x => x.AssetId).ToHashSet());
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
            .Select(x => $"{x.Ticker} recebeu {x.RecentContributionCount} aporte(s) recentemente e perdeu prioridade.")
            .ToList();
}

public sealed class DeterministicContributionIntelligenceProvider : IContributionIntelligenceProvider
{
    public Task<ContributionIntelligenceResult> EvaluateAsync(Guid portfolioId, decimal contributionAmount, IReadOnlyList<ContributionCandidate> candidates, CancellationToken cancellationToken = default)
        => Task.FromResult(new ContributionIntelligenceResult(Array.Empty<ContributionCandidateEvaluation>(), "Não há inteligência suficiente para diferenciar candidatos com segurança; foi aplicado o fallback determinístico.", ContributionIntelligenceAvailability.InsufficientIntelligence, new("R4-2.0", "deterministic", "none", "deterministic", "none", DateTimeOffset.UtcNow)));
}
