using Prumo.Domain.Enums;

namespace Prumo.Application.Contributions;

/// <summary>
/// Deterministic post-contribution state for one complete R5 allocation. It only
/// reports exposures backed by the portfolio metadata already available.
/// </summary>
public sealed record CompositionProjectionAllocation(string CandidateKey, string Ticker, decimal Amount);
public sealed record CompositionProjectionImpact(
    decimal ClassDeviationBefore,
    decimal ClassDeviationAfter,
    decimal ClassDeviationGain,
    decimal LargestPositionWeightBefore,
    decimal LargestPositionWeightAfter,
    decimal LargestPositionWeightGain,
    IReadOnlyList<PortfolioExposureImpact> ExposureImpacts);
public sealed record CompositionProjection(
    IReadOnlyList<CompositionProjectionAllocation> ProposedAllocation,
    PortfolioExposureSummary PortfolioBefore,
    PortfolioExposureSummary PortfolioAfter,
    CompositionProjectionImpact Impact,
    string ProjectionConfidence,
    IReadOnlyList<string> ProjectionWarnings);

public static class CompositionProjectionEvaluator
{
    public static CompositionProjection Project(IReadOnlyList<ContributionCandidate> candidates, IReadOnlyList<CandidateAllocation> allocations)
    {
        var byKey = candidates.ToDictionary(candidate => candidate.CandidateKey);
        var before = PortfolioExposureAggregator.Aggregate(candidates);
        var after = PortfolioExposureAggregator.Aggregate(candidates, allocations);
        var beforeDeviation = ContributionProjectionEvaluator.AggregateClassDeviation(candidates, Array.Empty<CandidateAllocation>());
        var afterDeviation = ContributionProjectionEvaluator.AggregateClassDeviation(candidates, allocations);
        var beforeLargest = LargestPositionWeight(candidates, Array.Empty<CandidateAllocation>());
        var afterLargest = LargestPositionWeight(candidates, allocations);
        var warnings = Warnings(before, after);

        return new CompositionProjection(
            allocations.Select(allocation => new CompositionProjectionAllocation(allocation.Key.Value, byKey[allocation.Key].Ticker, allocation.Amount)).ToArray(),
            before,
            after,
            new CompositionProjectionImpact(
                beforeDeviation,
                afterDeviation,
                beforeDeviation - afterDeviation,
                beforeLargest,
                afterLargest,
                beforeLargest - afterLargest,
                PortfolioExposureAggregator.Compare(before, after)),
            Confidence(before, after),
            warnings);
    }

    private static decimal LargestPositionWeight(IReadOnlyList<ContributionCandidate> candidates, IReadOnlyList<CandidateAllocation> allocations)
    {
        var additions = allocations.GroupBy(allocation => allocation.Key).ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));
        var total = candidates.Sum(candidate => candidate.CurrentValue) + additions.Values.Sum();
        return total <= 0 ? 0 : candidates.Max(candidate => (candidate.CurrentValue + additions.GetValueOrDefault(candidate.CandidateKey)) / total * 100m);
    }

    private static string Confidence(PortfolioExposureSummary before, PortfolioExposureSummary after)
    {
        var coverage = new[] { before.Sector.Coverage, before.Geography.Coverage, before.Currency.Coverage, after.Sector.Coverage, after.Geography.Coverage, after.Currency.Coverage };
        if (coverage.All(value => value == ExposureCoverage.Unavailable)) return "insufficient";
        if (coverage.Any(value => value == ExposureCoverage.Unavailable)) return "low";
        if (coverage.Any(value => value == ExposureCoverage.Partial)) return "medium";
        return "high";
    }

    private static IReadOnlyList<string> Warnings(PortfolioExposureSummary before, PortfolioExposureSummary after)
        => new[] { (before.Sector, after.Sector), (before.Geography, after.Geography), (before.Currency, after.Currency) }
            .Where(pair => pair.Item1.Coverage != ExposureCoverage.Complete || pair.Item2.Coverage != ExposureCoverage.Complete)
            .Select(pair => $"{pair.Item1.Dimension}: cobertura {pair.Item1.Coverage} antes e {pair.Item2.Coverage} depois; ausências não foram estimadas.")
            .ToArray();
}

public sealed record AllocationReview(
    string Decision,
    IReadOnlyList<AllocationProposalAllocation> Allocations,
    string Reason,
    IReadOnlyList<string> Considerations,
    ContributionIntelligenceAvailability Availability,
    ContributionIntelligenceMetadata Metadata);

public sealed record AllocationReviewResolution(
    IReadOnlyList<CandidateAllocation> FinalAllocations,
    bool CompositionChangedAfterProjection,
    bool ReviewAccepted,
    string Outcome,
    string? Warning);

public static class AllocationReviewResolver
{
    public static AllocationReviewResolution Resolve(
        IReadOnlyList<CandidateAllocation> initialAllocations,
        AllocationReview review,
        decimal contributionAmount,
        IReadOnlyList<ContributionCandidate> candidates)
    {
        if (string.Equals(review.Decision, "Confirm", StringComparison.OrdinalIgnoreCase))
        {
            if (review.Allocations.Count != 0)
                return KeepInitial(initialAllocations, "A revisão confirmou a proposta com alocações indevidas; a proposta inicial válida foi preservada.");
            return new(initialAllocations, false, true, "confirm", null);
        }

        if (!string.Equals(review.Decision, "Revise", StringComparison.OrdinalIgnoreCase))
            return KeepInitial(initialAllocations, "A revisão retornou uma decisão desconhecida; a proposta inicial válida foi preservada.");

        try
        {
            var revised = AllocationProposalValidator.Validate(
                new AllocationProposal("Allocate", review.Allocations, review.Reason, Array.Empty<string>(), "medium", review.Availability, review.Metadata),
                contributionAmount,
                candidates);
            var changed = !Same(initialAllocations, revised);
            return new(revised, changed, true, "revise", null);
        }
        catch (Exception exception)
        {
            return KeepInitial(initialAllocations, $"A revisão da IA foi inválida ({exception.Message}); a proposta inicial válida foi preservada.");
        }
    }

    private static AllocationReviewResolution KeepInitial(IReadOnlyList<CandidateAllocation> initial, string warning)
        => new(initial, false, false, "revise-invalid-kept-initial", warning);

    private static bool Same(IReadOnlyList<CandidateAllocation> left, IReadOnlyList<CandidateAllocation> right)
        => left.Count == right.Count
            && left.OrderBy(allocation => allocation.Key.Value, StringComparer.Ordinal).Zip(right.OrderBy(allocation => allocation.Key.Value, StringComparer.Ordinal))
                .All(pair => pair.First.Key == pair.Second.Key && pair.First.Amount == pair.Second.Amount);
}
