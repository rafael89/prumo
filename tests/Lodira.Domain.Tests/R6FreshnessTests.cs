using Lodira.Application.Contributions;
using Lodira.Application.Intelligence;
using Lodira.Application.Portfolio;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;

namespace Lodira.Domain.Tests;

public sealed class R6FreshnessTests
{
    [Fact]
    public void Snapshot_key_is_order_independent_but_changes_with_position_value()
    {
        var portfolio = new Portfolio("Carteira");
        var first = new Asset("AAA3", "Ação", AssetType.Stock, null);
        var second = new Asset("BBB3", "Ação 2", AssetType.Stock, null);
        var p1 = new PortfolioPosition(portfolio.Id, first.Id, 1, 10, 10);
        var p2 = new PortfolioPosition(portfolio.Id, second.Id, 2, 10, 20);
        portfolio.Positions.Add(p1); portfolio.Positions.Add(p2);
        var keyA = PortfolioSnapshotKeyCalculator.Calculate(portfolio);
        portfolio.Positions.Reverse();
        Assert.Equal(keyA, PortfolioSnapshotKeyCalculator.Calculate(portfolio));
        p1.Update(2, 10, 10);
        Assert.NotEqual(keyA, PortfolioSnapshotKeyCalculator.Calculate(portfolio));
    }

    [Fact]
    public void Equivalent_dependencies_are_fresh_and_legacy_metadata_is_unknown()
    {
        var portfolio = new Portfolio("Carteira");
        var asset = new Asset("AAA3", "Ação", AssetType.Stock, null);
        var position = new PortfolioPosition(portfolio.Id, asset.Id, 1, 10, 10);
        portfolio.Positions.Add(position);
        var eventItem = new InvestmentEvent(Guid.NewGuid(), InvestmentEventType.Other, null, "Fato", new[] { "Fato" });
        var snapshot = PortfolioSnapshotKeyCalculator.Calculate(portfolio);
        var assessment = new PortfolioAssessment(portfolio.Id, eventItem.Id, AttentionPriority.Low, ActionRequirement.None, CapitalDecisionImpact.None, AssessmentStatus.Active, AssessmentUserState.Unread, "Resumo", "Importa", "Acompanhar", IntelligenceConfidence.High, 1, null, PortfolioImpactAssessmentService.CurrentVersion, snapshot, eventItem.Version);
        var evaluator = new PortfolioAssessmentFreshnessEvaluator();
        Assert.Equal(FreshnessStatus.Fresh, evaluator.Evaluate(assessment, snapshot, 1, null, eventItem, PortfolioImpactAssessmentService.CurrentVersion).Status);
        var legacy = new PortfolioAssessment(portfolio.Id, eventItem.Id, AttentionPriority.Low, ActionRequirement.None, CapitalDecisionImpact.None, AssessmentStatus.Active, AssessmentUserState.Unread, "Resumo", "Importa", "Acompanhar", IntelligenceConfidence.High, 1, null);
        Assert.Equal(FreshnessStatus.Unknown, evaluator.Evaluate(legacy, snapshot, 1, null, eventItem, PortfolioImpactAssessmentService.CurrentVersion).Status);
    }

    [Theory]
    [InlineData(2, 1, "portfolio-impact-v1", FreshnessStatus.Stale)]
    [InlineData(1, 2, "portfolio-impact-v1", FreshnessStatus.Stale)]
    [InlineData(1, 1, "portfolio-impact-v2", FreshnessStatus.Stale)]
    public void Changed_dependency_is_stale(int currentStrategy, int currentObjective, string policy, FreshnessStatus expected)
    {
        var portfolio = new Portfolio("Carteira");
        var eventItem = new InvestmentEvent(Guid.NewGuid(), InvestmentEventType.Other, null, "Fato", new[] { "Fato" });
        var snapshot = PortfolioSnapshotKeyCalculator.Calculate(portfolio);
        var assessment = new PortfolioAssessment(portfolio.Id, eventItem.Id, AttentionPriority.Low, ActionRequirement.None, CapitalDecisionImpact.None, AssessmentStatus.Active, AssessmentUserState.Seen, "Resumo", "Importa", "Acompanhar", IntelligenceConfidence.High, 1, 1, PortfolioImpactAssessmentService.CurrentVersion, snapshot, eventItem.Version);
        var result = new PortfolioAssessmentFreshnessEvaluator().Evaluate(assessment, snapshot, currentStrategy, currentObjective, eventItem, policy);
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Cross_feature_health_and_guidance_keep_the_same_need_and_respect_block()
    {
        var portfolio = new Portfolio("Carteira");
        var fii = new Asset("TRXF11", "FII elegível", AssetType.Fii, null);
        var blocked = new Asset("HGLG11", "FII bloqueado", AssetType.Fii, null);
        var stock = new Asset("ACAO3", "Ação", AssetType.Stock, null);
        portfolio.Positions.Add(new PortfolioPosition(portfolio.Id, fii.Id, 320, 100, 100));
        portfolio.Positions.Add(new PortfolioPosition(portfolio.Id, blocked.Id, 0, 100, 0));
        portfolio.Positions.Add(new PortfolioPosition(portfolio.Id, stock.Id, 680, 100, 100));
        var strategy = new PortfolioAllocationStrategy(portfolio.Id, "Estratégia", null, 1, PriorityLevel.Medium, PriorityLevel.Medium, PriorityLevel.Medium, ContributionPolicy.CorrectImbalancesFirst, null);
        strategy.Allocations.Add(new StrategyAllocation(strategy.Id, AssetClass.Fii, 40));
        strategy.Allocations.Add(new StrategyAllocation(strategy.Id, AssetClass.BrazilianStock, 60));
        var health = new PortfolioHealthResult(PortfolioHealthStatus.Unbalanced, Array.Empty<AllocationDeviation>(), new MainDeviation(AssetClass.Fii, 32, 40, -8, AllocationDeviationDirection.Underweight), new[] { new CapitalNeed(AssetClass.Fii, 32, 40, 8, 1) }, Array.Empty<Concentration>(), "1", 1, DateTimeOffset.UtcNow);
        Assert.Equal(AssetClass.Fii, health.CapitalNeeds[0].AssetClass);

        var guidance = new ContributionGuidanceCalculator().Calculate(new ContributionGuidanceInput(
            portfolio.Id, 1_000, strategy, health,
            new[] { new GuidancePosition(fii.Id, fii.Ticker, AssetClass.Fii, 32_000, 32), new GuidancePosition(blocked.Id, blocked.Ticker, AssetClass.Fii, 0, 0), new GuidancePosition(stock.Id, stock.Ticker, AssetClass.BrazilianStock, 68_000, 68) },
            new[] { new GuidanceAssessment(blocked.Id, CapitalDecisionImpact.Block, AssessmentStatus.Active, "Evento contextual", PortfolioImpactAssessmentService.CurrentVersion) }));

        Assert.Contains(guidance.Allocations, x => x.AssetId == fii.Id);
        Assert.DoesNotContain(guidance.Allocations, x => x.AssetId == blocked.Id);
        Assert.Contains(guidance.AvoidedCandidates, x => x.AssetId == blocked.Id && x.ReasonType == ContributionAvoidanceReasonType.CapitalDecisionBlock);
    }
}
