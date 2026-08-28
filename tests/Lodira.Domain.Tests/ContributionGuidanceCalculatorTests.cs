using Lodira.Application.Contributions;
using Lodira.Domain.Enums;

namespace Lodira.Domain.Tests;

public sealed class ContributionGuidanceCalculatorTests
{
    private static ContributionPosition P(string ticker, decimal value, decimal target, bool review = false) => new(Guid.NewGuid(), ticker, value, target, review);

    [Fact]
    public void Above_target_receives_nothing_and_deficit_is_distributed()
    {
        var a = P("A", 800m, 50m); var b = P("B", 200m, 50m);
        var result = new ContributionGuidanceCalculator().Calculate(new[] { a, b }, 200m);
        Assert.Equal(0m, result.Items.Single(i => i.Ticker == "A").SuggestedContribution);
        Assert.Equal(200m, result.Items.Single(i => i.Ticker == "B").SuggestedContribution);
    }

    [Fact]
    public void Deficits_are_proportional_and_surplus_is_unallocated()
    {
        var result = new ContributionGuidanceCalculator().Calculate(new[] { P("A", 500, 50, true), P("B", 500, 50) }, 100);
        Assert.Equal(50m, result.AllocatedAmount);
        Assert.Equal(50m, result.UnallocatedAmount);
    }

    [Fact]
    public void Radar_review_blocks_contribution_without_changing_gap()
    {
        var result = new ContributionGuidanceCalculator().Calculate(new[] { P("A", 0, 100, true) }, 100);
        Assert.Equal(0m, result.Items[0].SuggestedContribution);
        Assert.Equal(ContributionEligibility.ReviewRequired, result.Items[0].Eligibility);
        Assert.Contains(ContributionReason.PendingRelevantRadarEvent, result.Items[0].Reasons);
    }

    [Fact]
    public void Cent_rounding_closes_the_requested_amount()
    {
        var result = new ContributionGuidanceCalculator().Calculate(new[] { P("A", 0, 33.33m), P("B", 0, 33.33m), P("C", 0, 33.34m) }, 1);
        Assert.Equal(result.RequestedAmount, result.AllocatedAmount + result.UnallocatedAmount);
        Assert.Equal(result.AllocatedAmount, result.Items.Sum(i => i.SuggestedContribution));
        Assert.Equal(1m, result.AllocatedAmount);
    }
}
