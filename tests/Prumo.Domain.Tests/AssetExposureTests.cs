using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

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
}
