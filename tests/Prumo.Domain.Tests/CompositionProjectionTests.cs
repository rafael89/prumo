using System.Text.Json;
using Prumo.Application.Contributions;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class CompositionProjectionTests
{
    [Fact]
    public void Projects_the_complete_multiclass_composition_without_inventing_unknown_exposures()
    {
        var stock = Candidate("AAA3", AssetClass.BrazilianStock, 1_000m) with
        {
            SectorCategory = EconomicSector.Energy,
            GeographicExposures = new[] { new KnownGeographicExposure("BR", 100m) },
            CurrencyExposures = new[] { new KnownCurrencyExposure("BRL", 100m) }
        };
        var fii = Candidate("BBB11", AssetClass.Fii, 1_000m) with { SectorCategory = EconomicSector.RealEstate };
        var allocations = new[] { new CandidateAllocation(stock.Identity, 3_150m), new CandidateAllocation(fii.Identity, 1_350m) };

        var projection = CompositionProjectionEvaluator.Project(new[] { stock, fii }, allocations);

        Assert.Equal(2, projection.ProposedAllocation.Count);
        Assert.Equal(2_000m, projection.PortfolioBefore.InvestedPortfolioValue);
        Assert.Equal(6_500m, projection.PortfolioAfter.InvestedPortfolioValue);
        Assert.NotEmpty(projection.Impact.ExposureImpacts);
        Assert.Equal(ExposureCoverage.Partial, projection.PortfolioAfter.Geography.Coverage);
        Assert.Equal(ExposureCoverage.Partial, projection.PortfolioAfter.Currency.Coverage);
        Assert.Contains(projection.ProjectionWarnings, warning => warning.Contains("Geography", StringComparison.Ordinal));
        Assert.DoesNotContain(projection.PortfolioAfter.Geography.Buckets, bucket => bucket.Key == "US");
        Assert.Equal("medium", projection.ProjectionConfidence);
    }

    [Fact]
    public void Review_can_confirm_or_replace_a_valid_composition_and_keeps_initial_when_revision_is_invalid()
    {
        var stock = Candidate("AAA3", AssetClass.BrazilianStock, 1_000m);
        var fii = Candidate("BBB11", AssetClass.Fii, 1_000m);
        var candidates = new[] { stock, fii };
        var initial = new[] { new CandidateAllocation(stock.Identity, 4_500m) };
        var metadata = new ContributionIntelligenceMetadata("R5-ai-allocation-v2", "policy", "review", "test", "model", DateTimeOffset.UtcNow);

        var confirmed = AllocationReviewResolver.Resolve(initial,
            new AllocationReview("Confirm", Array.Empty<AllocationProposalAllocation>(), "A concentração permanece a melhor opção pelos dados disponíveis.", Array.Empty<string>(), ContributionIntelligenceAvailability.IntelligenceAvailable, metadata),
            4_500m, candidates);
        var revised = AllocationReviewResolver.Resolve(initial,
            new AllocationReview("Revise", new[] { new AllocationProposalAllocation(stock.CandidateKey, 3_150m), new AllocationProposalAllocation(fii.CandidateKey, 1_350m) }, "A composição distribui os papéis marginais entre as classes.", Array.Empty<string>(), ContributionIntelligenceAvailability.IntelligenceAvailable, metadata),
            4_500m, candidates);
        var invalid = AllocationReviewResolver.Resolve(initial,
            new AllocationReview("Revise", new[] { new AllocationProposalAllocation(stock.CandidateKey, 4_501m) }, "A revisão excede o aporte.", Array.Empty<string>(), ContributionIntelligenceAvailability.IntelligenceAvailable, metadata),
            4_500m, candidates);

        Assert.True(confirmed.ReviewAccepted);
        Assert.False(confirmed.CompositionChangedAfterProjection);
        Assert.True(revised.ReviewAccepted);
        Assert.True(revised.CompositionChangedAfterProjection);
        Assert.Equal(2, revised.FinalAllocations.Count);
        Assert.False(invalid.ReviewAccepted);
        Assert.Single(invalid.FinalAllocations);
        Assert.Equal(stock.CandidateKey, invalid.FinalAllocations.Single().Key);
        Assert.Equal("revise-invalid-kept-initial", invalid.Outcome);
    }

    [Fact]
    public void Review_prompt_receives_full_post_contribution_state_and_original_candidates()
    {
        var stock = Candidate("AAA3", AssetClass.BrazilianStock, 1_000m) with
        {
            RecentContributionAmount = 500m,
            RecentContributionCount = 1,
            LastContributionAt = DateTimeOffset.UtcNow.AddDays(-10)
        };
        var fii = Candidate("BBB11", AssetClass.Fii, 1_000m);
        var alternativeStock = Candidate("CCC3", AssetClass.BrazilianStock, 500m);
        var alternativeFii = Candidate("DDD11", AssetClass.Fii, 500m);
        var candidates = new[] { stock, fii, alternativeStock, alternativeFii };
        var projection = CompositionProjectionEvaluator.Project(candidates, new[]
        {
            new CandidateAllocation(stock.Identity, 3_150m),
            new CandidateAllocation(fii.Identity, 1_350m)
        });

        var prompt = ContributionIntelligencePrompt.BuildAllocationReview(Guid.NewGuid(), 4_500m, candidates, Array.Empty<ContributionScenarioOption>(), InvestorPolicy.Current, projection);
        var context = prompt[(prompt.LastIndexOf("\n{") + 1)..];
        using var document = JsonDocument.Parse(context);

        Assert.Contains("efeito real pós-aporte", prompt);
        Assert.Contains("Confirm mantém exatamente proposedAllocation", prompt);
        Assert.Equal(2, document.RootElement.GetProperty("proposedAllocation").GetArrayLength());
        Assert.Equal(7_500m, document.RootElement.GetProperty("portfolioAfter").GetProperty("investedPortfolioValue").GetDecimal());
        Assert.Equal(4, document.RootElement.GetProperty("eligibleCandidates").GetArrayLength());
        Assert.Equal(1, document.RootElement.GetProperty("eligibleCandidates").EnumerateArray().Single(candidate => candidate.GetProperty("ticker").GetString() == "AAA3").GetProperty("recentContributionCount").GetInt32());
        Assert.True(document.RootElement.TryGetProperty("projectionWarnings", out _));
    }

    private static ContributionCandidate Candidate(string ticker, AssetClass assetClass, decimal currentValue)
        => new(CandidateIdentity.Known(Guid.NewGuid()), ticker, assetClass, currentValue, 50m, 50m, 50m, 4_500m, false, Array.Empty<ContributionAssessmentContext>())
        {
            HasPosition = currentValue > 0
        };
}
