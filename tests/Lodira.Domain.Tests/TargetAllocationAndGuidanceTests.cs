using Lodira.Application.Contributions;
using Lodira.Application.Portfolio;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Application.Explainability;

namespace Lodira.Domain.Tests;

public sealed class TargetAllocationAndGuidanceTests
{
    [Fact]
    public void Target_key_is_order_independent_and_changes_when_target_changes()
    {
        var p = Guid.NewGuid();
        var a = new[] { new TargetAllocation(p, AssetClass.Fii, 50), new TargetAllocation(p, AssetClass.FixedIncome, 50) };
        var b = new[] { new TargetAllocation(p, AssetClass.FixedIncome, 50), new TargetAllocation(p, AssetClass.Fii, 50) };
        Assert.Equal(TargetAllocationKeyCalculator.Calculate(a), TargetAllocationKeyCalculator.Calculate(b));
        var c = new[] { new TargetAllocation(p, AssetClass.Fii, 60), new TargetAllocation(p, AssetClass.FixedIncome, 40) };
        Assert.NotEqual(TargetAllocationKeyCalculator.Calculate(a), TargetAllocationKeyCalculator.Calculate(c));
    }

    [Fact]
    public void Health_uses_only_class_targets_and_rejects_unconfigured()
    {
        var result = new PortfolioHealthCalculator().Calculate(new Portfolio("Teste"), Array.Empty<TargetAllocation>());
        Assert.Equal(PortfolioHealthStatus.NotConfigured, result.Status);
    }

    [Fact]
    public void Applying_buy_updates_quantity_and_weighted_average_but_preserves_market_price()
    {
        var position = new PortfolioPosition(Guid.NewGuid(), Guid.NewGuid(), 10, 100, 120);
        position.ApplyBuy(5, 200);
        Assert.Equal(15, position.Quantity);
        Assert.Equal(2000m / 15m, position.AveragePrice);
        Assert.Equal(120, position.CurrentPrice);
        Assert.Equal(1800, position.CurrentValue);
    }

    [Fact]
    public void Applying_buy_rejects_non_positive_values()
    {
        var position = new PortfolioPosition(Guid.NewGuid(), Guid.NewGuid(), 1, 10, 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => position.ApplyBuy(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => position.ApplyBuy(1, 0));
    }

    [Fact]
    public void Intelligence_validation_rejects_unknown_blocked_and_over_budget_candidates()
    {
        var allowed = Guid.NewGuid(); var blocked = Guid.NewGuid();
        var candidates = new[] { new ContributionCandidate(allowed, "AAA", AssetClass.Fii, 100, 10, 50, 50, 1000, false, Array.Empty<ContributionAssessmentContext>()), new ContributionCandidate(blocked, "BBB", AssetClass.Fii, 100, 10, 50, 50, 1000, true, Array.Empty<ContributionAssessmentContext>()) };
        var result = new ContributionIntelligenceResult(new[] { new ContributionCandidateEvaluation(blocked, 1, 100, "x", 1, Array.Empty<EvidenceReference>()) }, "x", ContributionIntelligenceAvailability.IntelligenceAvailable, new("e", "p", "pr", "x", "m", DateTimeOffset.UtcNow));
        Assert.False(ContributionIntelligenceValidator.IsValid(result, 1000, candidates));
    }

    [Fact]
    public void Valid_intelligence_result_changes_choice_within_the_same_class()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var health = CreateHealth((AssetClass.Fii, 100, 100));
        var fallback = new ContributionGuidanceCalculator().Calculate(new ContributionGuidanceInput(Guid.NewGuid(), 100, health, new[] { new GuidancePosition(first, "AAA", AssetClass.Fii, 50, 50), new GuidancePosition(second, "BBB", AssetClass.Fii, 50, 50) }, Array.Empty<GuidanceAssessment>()));
        var result = new ContributionIntelligenceResult(new[] { new ContributionCandidateEvaluation(second, 1, 100, "Evidência mais favorável", .9m, new[] { new EvidenceReference("event", "e1", "Evento relevante") }) }, "BBB priorizado", ContributionIntelligenceAvailability.IntelligenceAvailable, new("e", "p", "pr", "Fake", "test", DateTimeOffset.UtcNow));
        var applied = ContributionIntelligenceApplier.Apply(fallback, result, new[] { new GuidancePosition(first, "AAA", AssetClass.Fii, 50, 50), new GuidancePosition(second, "BBB", AssetClass.Fii, 50, 50) });
        Assert.Equal(second, applied.Allocations.Single().AssetId);
        Assert.Equal("Evidência mais favorável", applied.Allocations.Single().Reason);
    }

    [Fact]
    public void Guidance_allocates_without_individual_asset_limit_and_preserves_sum()
    {
        var portfolioId = Guid.NewGuid();
        var health = new PortfolioHealthResult(PortfolioHealthStatus.Unbalanced,
            new[] { new AllocationDeviation(AssetClass.Fii, 0, 100, -100, 100, AllocationDeviationDirection.Underweight) },
            new MainDeviation(AssetClass.Fii, 0, 100, -100, AllocationDeviationDirection.Underweight),
            new[] { new CapitalNeed(AssetClass.Fii, 0, 100, 100, 1) }, "1", DateTimeOffset.UtcNow);
        var result = new ContributionGuidanceCalculator().Calculate(new ContributionGuidanceInput(
            portfolioId, 5000, health, new[] { new GuidancePosition(Guid.NewGuid(), "FII", AssetClass.Fii, 14000, 14) }, Array.Empty<GuidanceAssessment>()));
        Assert.Equal(5000, result.AllocatedAmount);
        Assert.Equal(0, result.UnallocatedAmount);
    }

    [Fact]
    public void Block_leaves_capital_unallocated()
    {
        var id = Guid.NewGuid();
        var health = new PortfolioHealthResult(PortfolioHealthStatus.Unbalanced, Array.Empty<AllocationDeviation>(), null,
            new[] { new CapitalNeed(AssetClass.Fii, 0, 100, 100, 1) }, "1", DateTimeOffset.UtcNow);
        var result = new ContributionGuidanceCalculator().Calculate(new ContributionGuidanceInput(
            Guid.NewGuid(), 5000, health, new[] { new GuidancePosition(id, "FII", AssetClass.Fii, 100, 1) },
            new[] { new GuidanceAssessment(id, CapitalDecisionImpact.Block, AssessmentStatus.Detected, "e", "v1") }));
        Assert.Equal(5000, result.UnallocatedAmount);
        Assert.Empty(result.Allocations);
    }

    [Fact]
    public void Guidance_distributes_aligned_50_50_contribution_using_projected_targets()
    {
        var result = Calculate(4000, (AssetClass.BrazilianStock, 50000, 50m), (AssetClass.Fii, 50000, 50m));
        Assert.Equal(2000, result.Allocations.Single(x => x.AssetClass == AssetClass.BrazilianStock).Amount);
        Assert.Equal(2000, result.Allocations.Single(x => x.AssetClass == AssetClass.Fii).Amount);
        Assert.Equal(0, result.UnallocatedAmount);
    }

    [Fact]
    public void Guidance_distributes_aligned_60_40_contribution_using_projected_targets()
    {
        var result = Calculate(10000, (AssetClass.BrazilianStock, 60000, 60m), (AssetClass.Fii, 40000, 40m));
        Assert.Equal(6000, result.Allocations.Single(x => x.AssetClass == AssetClass.BrazilianStock).Amount);
        Assert.Equal(4000, result.Allocations.Single(x => x.AssetClass == AssetClass.Fii).Amount);
    }

    [Fact]
    public void Guidance_corrects_existing_imbalance_before_preserving_targets()
    {
        var result = Calculate(4000, (AssetClass.BrazilianStock, 40000, 50m), (AssetClass.Fii, 60000, 50m));
        Assert.Equal(4000, result.Allocations.Single(x => x.AssetClass == AssetClass.BrazilianStock).Amount);
        Assert.DoesNotContain(result.Allocations, x => x.AssetClass == AssetClass.Fii);
    }

    [Fact]
    public void Guidance_distributes_remaining_capital_after_correcting_imbalance()
    {
        var result = Calculate(30000, (AssetClass.BrazilianStock, 40000, 50m), (AssetClass.Fii, 60000, 50m));
        Assert.Equal(25000, result.Allocations.Single(x => x.AssetClass == AssetClass.BrazilianStock).Amount);
        Assert.Equal(5000, result.Allocations.Single(x => x.AssetClass == AssetClass.Fii).Amount);
        Assert.Equal(30000, result.AllocatedAmount);
    }

    [Fact]
    public void Guidance_does_not_redirect_projected_need_when_candidate_is_blocked()
    {
        var stockId = Guid.NewGuid();
        var fiiId = Guid.NewGuid();
        var health = CreateHealth((AssetClass.BrazilianStock, 50000, 50m), (AssetClass.Fii, 50000, 50m));
        var result = new ContributionGuidanceCalculator().Calculate(new ContributionGuidanceInput(
            Guid.NewGuid(), 4000, health,
            new[] { new GuidancePosition(stockId, "AAA", AssetClass.BrazilianStock, 50000, 50), new GuidancePosition(fiiId, "BBB", AssetClass.Fii, 50000, 50) },
            new[] { new GuidanceAssessment(stockId, CapitalDecisionImpact.Block, AssessmentStatus.Detected, "block", "v1") }));
        Assert.Equal(2000, result.Allocations.Single().Amount);
        Assert.Equal(2000, result.UnallocatedAmount);
    }

    private static ContributionGuidance Calculate(decimal amount, params (AssetClass AssetClass, decimal CurrentValue, decimal TargetPercentage)[] values)
    {
        var health = CreateHealth(values);
        var total = values.Sum(value => value.CurrentValue);
        var positions = values.Select((value, index) => new GuidancePosition(
            Guid.NewGuid(), $"{value.AssetClass}-{index}", value.AssetClass, value.CurrentValue, value.CurrentValue / total * 100m)).ToArray();
        return new ContributionGuidanceCalculator().Calculate(new ContributionGuidanceInput(Guid.NewGuid(), amount, health, positions, Array.Empty<GuidanceAssessment>()));
    }

    private static PortfolioHealthResult CreateHealth(params (AssetClass AssetClass, decimal CurrentValue, decimal TargetPercentage)[] values)
    {
        var total = values.Sum(value => value.CurrentValue);
        var deviations = values.Select(value =>
        {
            var current = value.CurrentValue / total * 100m;
            var deviation = current - value.TargetPercentage;
            var absolute = Math.Abs(deviation);
            var direction = absolute <= 2 ? AllocationDeviationDirection.Aligned : current < value.TargetPercentage ? AllocationDeviationDirection.Underweight : AllocationDeviationDirection.Overweight;
            return new AllocationDeviation(value.AssetClass, current, value.TargetPercentage, deviation, absolute, direction);
        }).ToArray();
        return new PortfolioHealthResult(PortfolioHealthStatus.Aligned, deviations, null, Array.Empty<CapitalNeed>(), "1", DateTimeOffset.UtcNow);
    }
}
