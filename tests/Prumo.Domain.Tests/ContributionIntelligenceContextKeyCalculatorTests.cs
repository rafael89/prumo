using Prumo.Application.Contributions;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class ContributionIntelligenceContextKeyCalculatorTests
{
    [Fact]
    public void KeyIsStableRegardlessOfAssessmentOrder()
    {
        var first = Assessment(Guid.NewGuid(), CapitalDecisionImpact.Favor, "assessment-v1");
        var second = Assessment(Guid.NewGuid(), CapitalDecisionImpact.Caution, "assessment-v2");

        var forward = ContributionIntelligenceContextKeyCalculator.Calculate(new[] { first, second });
        var reverse = ContributionIntelligenceContextKeyCalculator.Calculate(new[] { second, first });

        Assert.Equal(forward, reverse);
    }

    [Fact]
    public void KeyChangesWhenAssessmentImpactChanges()
    {
        var assetId = Guid.NewGuid();
        var neutral = Assessment(assetId, CapitalDecisionImpact.None, "assessment-v1");
        var blocked = Assessment(assetId, CapitalDecisionImpact.Block, "assessment-v1");

        Assert.NotEqual(
            ContributionIntelligenceContextKeyCalculator.Calculate(new[] { neutral }),
            ContributionIntelligenceContextKeyCalculator.Calculate(new[] { blocked }));
    }

    [Fact]
    public void KeyChangesForANewAssessmentEvenWhenItsContentIsEqual()
    {
        var assetId = Guid.NewGuid();
        var first = Assessment(assetId, CapitalDecisionImpact.Caution, "assessment-v1", Guid.NewGuid());
        var newer = Assessment(assetId, CapitalDecisionImpact.Caution, "assessment-v1", Guid.NewGuid());

        Assert.NotEqual(
            ContributionIntelligenceContextKeyCalculator.Calculate(new[] { first }),
            ContributionIntelligenceContextKeyCalculator.Calculate(new[] { newer }));
    }

    private static GuidanceAssessment Assessment(Guid assetId, CapitalDecisionImpact impact, string version, Guid assessmentId = default) => new(
        assetId,
        impact,
        AssessmentStatus.Detected,
        "Documento de teste",
        version,
        AttentionPriority.Medium,
        ActionRequirement.Monitor,
        "Resumo",
        "Impacto",
        "Acompanhar",
        0.8m,
        assessmentId);
}
