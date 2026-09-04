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
            new ContributionCandidate(id, "AAA", AssetClass.Fii, 20, 20, 20, 50, 0, false, Array.Empty<ContributionAssessmentContext>()),
            new ContributionCandidate(other, "BBB", AssetClass.BrazilianStock, 80, 80, 80, 50, 0, false, Array.Empty<ContributionAssessmentContext>())
        }, new[] { (id, 100m) });
        Assert.Equal(PortfolioDecisionType.ReinforceExisting, result.DecisionType);
    }

    [Fact]
    public void Chooses_add_new_for_known_unheld_asset()
    {
        var id = Guid.NewGuid();
        var result = Decide(new ContributionCandidate(id, "AAA", AssetClass.Fii, 0, 0, 0, 100, 0, false, Array.Empty<ContributionAssessmentContext>(), HasPosition: false), (id, 100));
        Assert.Equal(PortfolioDecisionType.AddNewAsset, result.DecisionType);
    }

    [Fact]
    public void Returns_no_action_when_balanced()
    {
        var id = Guid.NewGuid();
        var result = Decide(new ContributionCandidate(id, "AAA", AssetClass.Fii, 100, 100, 100, 100, 0, false, Array.Empty<ContributionAssessmentContext>()), (id, 100));
        Assert.Equal(PortfolioDecisionType.NoAction, result.DecisionType);
    }

    [Fact]
    public void Chooses_split_when_recommended_allocations_have_two_assets()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var candidates = new[]
        {
            new ContributionCandidate(first, "AAA", AssetClass.Fii, 10, 10, 10, 50, 0, false, Array.Empty<ContributionAssessmentContext>()),
            new ContributionCandidate(second, "BBB", AssetClass.Fii, 10, 10, 10, 50, 0, false, Array.Empty<ContributionAssessmentContext>()),
            new ContributionCandidate(Guid.NewGuid(), "CCC", AssetClass.BrazilianStock, 80, 80, 80, 50, 0, false, Array.Empty<ContributionAssessmentContext>())
        };
        var result = new PortfolioDecisionService().Decide(100, candidates, new[] { (first, 50m), (second, 50m) });
        Assert.Equal(PortfolioDecisionType.SplitContribution, result.DecisionType);
    }

    private static PortfolioDecision Decide(ContributionCandidate candidate, (Guid AssetId, decimal Amount) allocation)
        => new PortfolioDecisionService().Decide(100, new[] { candidate }, new[] { allocation });
}
