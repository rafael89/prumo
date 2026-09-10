using Prumo.Application.Abstractions;
using Prumo.Application.Contributions;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class FundOfFundsSectorSemanticsTests
{
    [Fact]
    public void FundOfFunds_without_lookthrough_has_neutral_sector_score()
    {
        var candidate = Candidate("HFOF11", "Fund of Funds", 6.82m, EconomicSector.RealEstate);

        Assert.Equal(FundType.FundOfFunds, SectorExposureSemantics.InferFundType(AssetType.Fii, candidate.Sector));
        Assert.Equal(UnderlyingSectorExposureStatus.Unavailable, candidate.UnderlyingSectorExposure.Status);
        Assert.Equal(0m, candidate.EffectiveDiversificationScore);
        Assert.Equal(0m, candidate.DiversificationScore);
    }

    [Fact]
    public void Direct_brick_sector_keeps_diversification_calculation()
    {
        var candidate = Candidate("FIIB11", "Industrial Real Estate", 25m, EconomicSector.RealEstate);

        Assert.Equal(75m, candidate.EffectiveDiversificationScore);
    }

    [Fact]
    public void FundOfFunds_is_not_an_economic_sector_bucket()
    {
        var exposure = PortfolioExposureAggregator.Aggregate(new[]
        {
            Candidate("HFOF11", "Fund of Funds", 6.82m, EconomicSector.RealEstate) with { CurrentValue = 100 },
            Candidate("FIIB11", "Industrial Real Estate", 93.18m, EconomicSector.RealEstate) with { CurrentValue = 100 }
        });

        Assert.Single(exposure.Sector.Buckets);
        Assert.Equal("RealEstate", exposure.Sector.Buckets[0].Key);
        Assert.Equal(50m, exposure.Sector.Buckets[0].PortfolioPercentage);
    }

    [Fact]
    public void R5_prompt_explains_the_neutral_unknown_semantics()
    {
        var prompt = ContributionIntelligencePrompt.BuildAllocationProposal(
            Guid.NewGuid(), 100, new[] { Candidate("HFOF11", "Fund of Funds", 6.82m, EconomicSector.RealEstate) },
            Array.Empty<ContributionScenarioOption>());

        Assert.Contains("FundOfFunds is a fund structure, not an economic sector", prompt);
        Assert.Contains("underlyingSectorExposure", prompt);
        Assert.Contains("sector diversification effect as unknown", prompt);
        Assert.Contains("\"sectorWeightWithinClass\":null", prompt);
        Assert.Contains("\"diversificationScore\":0", prompt);
        Assert.Contains("\"underlyingSectorExposure\":{\"status\":\"Unavailable\"", prompt);
    }

    private static ContributionCandidate Candidate(string ticker, string sector, decimal sectorWeight, EconomicSector category) =>
        new(CandidateIdentity.Known(Guid.NewGuid()), ticker, AssetClass.Fii, 0, 0, 100, 100, 100, false,
            Array.Empty<ContributionAssessmentContext>(), AssetType.Fii, sector, SectorWeightWithinClass: sectorWeight,
            SectorCategory: category);
}
