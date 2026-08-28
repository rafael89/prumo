using Lodira.Domain.Entities;
using Lodira.Domain.Enums;

namespace Lodira.Domain.Tests;

public sealed class AssetClassTests
{
    [Theory]
    [InlineData(AssetType.Stock, AssetClass.BrazilianStock)]
    [InlineData(AssetType.Fii, AssetClass.Fii)]
    [InlineData(AssetType.Etf, AssetClass.Other)]
    [InlineData(AssetType.Other, AssetClass.Other)]
    public void New_asset_gets_explicit_safe_class_from_asset_type(AssetType type, AssetClass expected)
    {
        var asset = new Asset("TEST", "Test asset", type, null);

        Assert.Equal(expected, asset.AssetClass);
    }

    [Fact]
    public void New_asset_can_receive_an_explicit_class_when_asset_type_is_not_sufficient()
    {
        var asset = new Asset("BOND", "Fixed income", AssetType.Other, null, AssetClass.FixedIncome);

        Assert.Equal(AssetClass.FixedIncome, asset.AssetClass);
    }
}
