using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.MarketData;

namespace Prumo.Domain.Tests;

public sealed class AssetExposureTests
{
    [Fact]
    public void Preserves_partial_geographic_exposure_and_provenance()
    {
        var asset = new Asset("IVVB11", "ETF internacional", AssetType.Etf, null);
        var exposure = new AssetGeographicExposure(asset.Id, "US", 60m, ExposureSource.Provider, "provider:ivvb11", DateTimeOffset.UtcNow);

        asset.GeographicExposures.Add(exposure);

        Assert.Equal("US", exposure.CountryCode);
        Assert.Equal(60m, exposure.Percentage);
        Assert.Equal(ExposureSource.Provider, exposure.Source);
        Assert.Equal(AssetClass.International, asset.AssetClass);
        Assert.Empty(asset.CurrencyExposures);
    }

    [Fact]
    public void Rejects_invalid_exposure_codes_and_percentages()
    {
        var assetId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => new AssetGeographicExposure(assetId, "Brasil", null));
        Assert.Throws<ArgumentException>(() => new AssetCurrencyExposure(assetId, "R$", null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AssetGeographicExposure(assetId, "BR", 100.01m));
    }

    [Fact]
    public void Curated_catalog_adds_reviewable_facts_without_false_economic_precision()
    {
        var petr4 = new Asset("PETR4", "Petrobras", AssetType.Stock, null);
        var ivvb11 = new Asset("IVVB11", "iShares S&P 500", AssetType.Etf, null);

        Assert.True(AssetMetadataCatalog.Apply(petr4));
        Assert.Equal("Energy", petr4.Sector);
        Assert.Equal(EconomicSector.Energy, petr4.SectorCategory);
        Assert.Equal("BR", petr4.ListingCountryCode);
        Assert.Equal("BRL", petr4.TradingCurrency);
        Assert.Empty(petr4.GeographicExposures);
        Assert.Empty(petr4.CurrencyExposures);

        Assert.True(AssetMetadataCatalog.Apply(ivvb11));
        Assert.Equal(ExposureCoverage.Complete, ivvb11.GeographicExposureCoverage);
        Assert.Equal("US", ivvb11.GeographicExposures.Single().CountryCode);
        Assert.Equal("USD", ivvb11.CurrencyExposures.Single().CurrencyCode);
        Assert.Equal(ExposureSource.Manual, ivvb11.GeographicExposures.Single().Source);
        Assert.Contains(AssetMetadataCatalog.Version, ivvb11.GeographicExposures.Single().SourceReference);
    }

    [Theory]
    [InlineData("AFHI11", "Real Estate Receivables")]
    [InlineData("BTLG11", "Logistics Real Estate")]
    [InlineData("HGBS11", "Shopping Centers")]
    [InlineData("HFOF11", "Fund of Funds")]
    [InlineData("TRXF11", "Urban Retail Real Estate")]
    [InlineData("XPIN11", "Industrial Real Estate")]
    public void Curated_catalog_classifies_current_portfolio_fii_strategies(string ticker, string sector)
    {
        var asset = new Asset(ticker, ticker, AssetType.Fii, null);

        Assert.True(AssetMetadataCatalog.Apply(asset));
        Assert.Equal(sector, asset.Sector);
        Assert.Equal(EconomicSector.RealEstate, asset.SectorCategory);
        Assert.Equal("BR", asset.ListingCountryCode);
        Assert.Equal("BRL", asset.TradingCurrency);
        Assert.Equal(ExposureCoverage.Unavailable, asset.GeographicExposureCoverage);
        Assert.Equal(ExposureCoverage.Unavailable, asset.EconomicCurrencyExposureCoverage);
    }
}
