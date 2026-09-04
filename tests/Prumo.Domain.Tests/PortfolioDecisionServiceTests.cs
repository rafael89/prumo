using Prumo.Application.Contributions;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class PortfolioDecisionServiceTests
{
    [Fact]
    public void Chooses_reinforce_for_one_held_asset_when_projection_improves()
    {
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        var result = new PortfolioDecisionService().Decide(100, new[]
        {
            new ContributionCandidate(CandidateIdentity.Known(id), "AAA", AssetClass.Fii, 20, 20, 20, 50, 0, false, Array.Empty<ContributionAssessmentContext>()),
            new ContributionCandidate(CandidateIdentity.Known(other), "BBB", AssetClass.BrazilianStock, 80, 80, 80, 50, 0, false, Array.Empty<ContributionAssessmentContext>())
        }, new[] { (id, 100m) });
        Assert.Equal(PortfolioDecisionType.ReinforceExisting, result.DecisionType);
    }

    [Fact]
    public void Chooses_add_new_for_known_unheld_asset()
    {
        var id = Guid.NewGuid();
        var result = Decide(new ContributionCandidate(CandidateIdentity.Known(id), "AAA", AssetClass.Fii, 0, 0, 0, 100, 0, false, Array.Empty<ContributionAssessmentContext>(), HasPosition: false), (id, 100));
        Assert.Equal(PortfolioDecisionType.AddNewAsset, result.DecisionType);
    }

    [Fact]
    public void Returns_no_action_when_balanced()
    {
        var id = Guid.NewGuid();
        var result = Decide(new ContributionCandidate(CandidateIdentity.Known(id), "AAA", AssetClass.Fii, 100, 100, 100, 100, 0, false, Array.Empty<ContributionAssessmentContext>()), (id, 100));
        Assert.Equal(PortfolioDecisionType.NoAction, result.DecisionType);
    }

    [Fact]
    public void Chooses_split_when_recommended_allocations_have_two_assets()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var candidates = new[]
        {
            new ContributionCandidate(CandidateIdentity.Known(first), "AAA", AssetClass.Fii, 10, 10, 10, 50, 0, false, Array.Empty<ContributionAssessmentContext>()),
            new ContributionCandidate(CandidateIdentity.Known(second), "BBB", AssetClass.Fii, 10, 10, 10, 50, 0, false, Array.Empty<ContributionAssessmentContext>()),
            new ContributionCandidate(CandidateIdentity.Known(Guid.NewGuid()), "CCC", AssetClass.BrazilianStock, 80, 80, 80, 50, 0, false, Array.Empty<ContributionAssessmentContext>())
        };
        var result = new PortfolioDecisionService().Decide(100, candidates, new[] { (first, 50m), (second, 50m) });
        Assert.Equal(PortfolioDecisionType.SplitContribution, result.DecisionType);
    }

    [Fact]
    public void Prefers_same_class_alternative_that_does_not_worsen_severe_concentration()
    {
        var concentrated = Guid.NewGuid();
        var alternative = Guid.NewGuid();
        var otherClass = Guid.NewGuid();
        var candidates = new[]
        {
            new ContributionCandidate(CandidateIdentity.Known(concentrated), "AAA", AssetClass.Fii, 60, 60, 70, 80, 100, false, Array.Empty<ContributionAssessmentContext>()),
            new ContributionCandidate(CandidateIdentity.Known(alternative), "BBB", AssetClass.Fii, 10, 10, 70, 80, 100, false, Array.Empty<ContributionAssessmentContext>()),
            new ContributionCandidate(CandidateIdentity.Known(otherClass), "CCC", AssetClass.BrazilianStock, 30, 30, 30, 20, 0, false, Array.Empty<ContributionAssessmentContext>())
        };

        var result = new PortfolioDecisionService().Decide(100, candidates, new[] { (concentrated, 100m) });

        Assert.Equal(alternative, result.RecommendedScenario!.Allocations.Single().AssetId);
    }

    [Fact]
    public void Exposes_structured_evidence_for_the_decisive_factor()
    {
        var id = Guid.NewGuid();
        var result = Decide(new ContributionCandidate(CandidateIdentity.Known(id), "AAA", AssetClass.Fii, 0, 0, 0, 100, 100, false, Array.Empty<ContributionAssessmentContext>(), HasPosition: false), (id, 100));

        Assert.NotNull(result.DecisionEvidence);
        Assert.Equal(CandidateStructuralFit.DirectlyAddressesPrimaryNeed, result.DecisionEvidence!.StructuralFit);
        Assert.Equal("PrimaryNeedImprovement", result.DecisionEvidence.DecisiveFactor);
    }

    [Fact]
    public void Deterministic_selection_is_independent_of_candidate_order_and_origin()
    {
        var external = CandidateIdentity.FromExternal(new("Brapi", "EXT11", "B3", AssetType.Stock));
        var known = CandidateIdentity.Known(Guid.NewGuid());
        var candidates = new[] { Candidate(known, "ZZZ"), Candidate(external, "AAA") };
        var first = new PortfolioDecisionService().DecideDeterministically(100, candidates);
        var second = new PortfolioDecisionService().DecideDeterministically(100, candidates.Reverse().ToArray());

        Assert.Equal(first.RecommendedScenario!.Allocations.Select(x => x.Key), second.RecommendedScenario!.Allocations.Select(x => x.Key));
        Assert.Equal(first.DecisionEvidence!.DecisiveFactor, second.DecisionEvidence!.DecisiveFactor);
        Assert.Equal(external.Key, first.RecommendedScenario.Allocations.Single().Key);
    }

    [Fact]
    public void Generates_a_mathematical_split_for_two_class_deficits()
    {
        var first = CandidateIdentity.Known(Guid.NewGuid());
        var second = CandidateIdentity.FromExternal(new("Brapi", "EXT12", "B3", AssetType.Stock));
        var result = new PortfolioDecisionService().DecideDeterministically(100, new[]
        {
            new ContributionCandidate(first, "AAA", AssetClass.Fii, 0, 0, 0, 60, 100, false, Array.Empty<ContributionAssessmentContext>()),
            new ContributionCandidate(second, "BBB", AssetClass.BrazilianStock, 0, 0, 0, 40, 100, false, Array.Empty<ContributionAssessmentContext>())
        });

        Assert.Equal(PortfolioDecisionType.SplitContribution, result.DecisionType);
        Assert.Equal(100m, result.RecommendedScenario!.Allocations.Sum(x => x.Amount));
        Assert.Equal(60m, result.RecommendedScenario.Allocations.Single(x => x.Key == first.Key).Amount);
        Assert.Equal(40m, result.RecommendedScenario.Allocations.Single(x => x.Key == second.Key).Amount);
    }

    [Fact]
    public void Uses_supported_quality_only_after_structural_tie()
    {
        var stronger = Guid.NewGuid();
        var weaker = Guid.NewGuid();
        var snapshot = new CandidateFinancialSnapshot(DateOnly.FromDateTime(DateTime.UtcNow), FinancialPeriodType.Ttm, DateTimeOffset.UtcNow, FinancialDataSource.Provider, NetIncome: 10, Equity: 100);
        var result = new PortfolioDecisionService().DecideDeterministically(100, new[]
        {
            new ContributionCandidate(CandidateIdentity.Known(stronger), "AAA", AssetClass.BrazilianStock, 0, 0, 0, 100, 100, false, Array.Empty<ContributionAssessmentContext>(), AssetType.Stock, FinancialSnapshot: snapshot),
            new ContributionCandidate(CandidateIdentity.Known(weaker), "ZZZ", AssetClass.BrazilianStock, 0, 0, 0, 100, 100, false, Array.Empty<ContributionAssessmentContext>(), AssetType.Stock, FinancialSnapshot: snapshot with { NetIncome = -10 })
        });

        Assert.Equal(CandidateIdentity.Known(stronger).Key, result.RecommendedScenario!.Allocations.Single().Key);
        Assert.Equal("FinancialQuality", result.DecisionEvidence!.DecisiveFactor);
    }

    private static ContributionCandidate Candidate(CandidateIdentity identity, string ticker) => new(identity, ticker, AssetClass.BrazilianStock, 0, 0, 0, 100, 100, false, Array.Empty<ContributionAssessmentContext>(), AssetType.Stock, HasPosition: identity.Origin == CandidateOrigin.KnownCatalog, AllocationNeedScore: 100, UnderweightScore: 100);

    private static PortfolioDecision Decide(ContributionCandidate candidate, (Guid AssetId, decimal Amount) allocation)
        => new PortfolioDecisionService().Decide(100, new[] { candidate }, new[] { allocation });
}
