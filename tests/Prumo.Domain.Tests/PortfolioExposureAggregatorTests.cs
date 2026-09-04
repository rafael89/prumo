using Prumo.Application.Contributions;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class PortfolioExposureAggregatorTests
{
    [Fact]
    public void Keeps_unknown_remainder_without_normalizing_partial_data()
    {
        var candidates = new[]
        {
            Candidate(50, Geographic: new[] { new KnownGeographicExposure("US", 50) }),
            Candidate(50, Geographic: new[] { new KnownGeographicExposure("US", 100) })
        };

        var result = PortfolioExposureAggregator.Aggregate(candidates);
        var geography = result.Geography;

        Assert.Equal(75m, geography.Buckets.Single().PortfolioPercentage);
        Assert.Equal(75m, geography.KnownPortfolioPercentage);
        Assert.Equal(25m, geography.UnknownPortfolioPercentage);
        Assert.Equal(ExposureCoverage.Partial, geography.Coverage);
    }

    [Fact]
    public void Aggregates_multiple_known_geographies_and_detects_concentration()
    {
        var candidates = new[]
        {
            Candidate(70, Geographic: new[] { new KnownGeographicExposure("BR", 100) }),
            Candidate(30, Geographic: new[] { new KnownGeographicExposure("US", 100) }, Sector: EconomicSector.Technology)
        };

        var result = PortfolioExposureAggregator.Aggregate(candidates);
        var needs = PortfolioExposureAggregator.DetectNeeds(candidates, result);

        Assert.Equal(70m, result.Geography.Buckets.Single(x => x.Key == "BR").PortfolioPercentage);
        Assert.Equal(30m, result.Geography.Buckets.Single(x => x.Key == "US").PortfolioPercentage);
        Assert.Equal(ExposureCoverage.Complete, result.Geography.Coverage);
        Assert.Contains(needs, x => x.Type == "SingleAssetConcentration");
        Assert.Contains(needs, x => x.Type == "GeographicConcentration" && x.Key == "BR");
    }

    [Fact]
    public void Does_not_use_listing_country_or_trading_currency_as_economic_exposure()
    {
        // Listing country and trading currency are intentionally not inputs to economic aggregation.
        var candidate = Candidate(100);

        var result = PortfolioExposureAggregator.Aggregate(new[] { candidate });

        Assert.Equal(ExposureCoverage.Unavailable, result.Geography.Coverage);
        Assert.Equal(ExposureCoverage.Unavailable, result.Currency.Coverage);
        Assert.Equal(100m, result.Geography.UnknownPortfolioPercentage);
        Assert.Equal(100m, result.Currency.UnknownPortfolioPercentage);
    }

    private static ContributionCandidate Candidate(decimal currentValue, EconomicSector? Sector = null, IReadOnlyList<KnownGeographicExposure>? Geographic = null)
        => new(CandidateIdentity.Known(Guid.NewGuid()), "TEST", AssetClass.BrazilianStock, currentValue, 100, 100, 100, 0, false, Array.Empty<ContributionAssessmentContext>(), SectorCategory: Sector, GeographicExposures: Geographic);
}
