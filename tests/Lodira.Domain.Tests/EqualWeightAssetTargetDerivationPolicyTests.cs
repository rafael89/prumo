using Lodira.Application.Contributions;
using Lodira.Domain.Enums;

namespace Lodira.Domain.Tests;

public sealed class EqualWeightAssetTargetDerivationPolicyTests
{
    private static AssetForTarget Asset(string ticker, AssetClass assetClass, decimal value = 100) => new(Guid.NewGuid(), ticker, assetClass, value);

    [Fact]
    public void One_asset_receives_the_whole_class_target()
    {
        var result = Derive(new[] { Asset("BBSE3", AssetClass.BrazilianStock) }, (AssetClass.BrazilianStock, 100));
        Assert.Equal(100m, result.Single().TargetPercentage);
    }

    [Fact]
    public void Class_target_is_split_equally_between_two_assets()
    {
        var result = Derive(new[] { Asset("BBSE3", AssetClass.BrazilianStock), Asset("ITUB4", AssetClass.BrazilianStock) }, (AssetClass.BrazilianStock, 100));
        Assert.All(result, item => Assert.Equal(50m, item.TargetPercentage));
    }

    [Fact]
    public void Multiple_classes_are_derived_independently()
    {
        var result = Derive(new[] { Asset("BBSE3", AssetClass.BrazilianStock), Asset("ITUB4", AssetClass.BrazilianStock), Asset("XPLG11", AssetClass.Fii), Asset("HGLG11", AssetClass.Fii) },
            (AssetClass.BrazilianStock, 60), (AssetClass.Fii, 40));
        Assert.Equal(30m, result.Single(item => item.Ticker == "BBSE3").TargetPercentage);
        Assert.Equal(20m, result.Single(item => item.Ticker == "XPLG11").TargetPercentage);
    }

    [Fact]
    public void Non_exact_split_keeps_full_internal_precision()
    {
        var result = Derive(new[] { Asset("A", AssetClass.Fii), Asset("B", AssetClass.Fii), Asset("C", AssetClass.Fii) }, (AssetClass.Fii, 40));
        Assert.Equal(40m, result.Sum(item => item.TargetPercentage));
    }

    [Fact]
    public void Adding_or_removing_an_asset_recalculates_equal_weights()
    {
        var policy = new EqualWeightAssetTargetDerivationPolicy();
        var a = Asset("A", AssetClass.Fii); var b = Asset("B", AssetClass.Fii);
        Assert.Equal(50m, policy.Derive(new[] { a, b }, new[] { (AssetClass.Fii, 100m) }).First().TargetPercentage);
        Assert.Equal(100m, policy.Derive(new[] { a }, new[] { (AssetClass.Fii, 100m) }).First().TargetPercentage);
    }

    [Fact]
    public void Unconfigured_class_gets_zero_without_redistribution()
    {
        var result = Derive(new[] { Asset("A", AssetClass.BrazilianStock), Asset("T", AssetClass.FixedIncome) }, (AssetClass.BrazilianStock, 80));
        Assert.Equal(0m, result.Single(item => item.Ticker == "T").TargetPercentage);
        Assert.Equal(80m, result.Sum(item => item.TargetPercentage));
    }

    private static IReadOnlyList<DerivedAssetTarget> Derive(IEnumerable<AssetForTarget> assets, params (AssetClass AssetClass, decimal TargetPercentage)[] allocations)
        => new EqualWeightAssetTargetDerivationPolicy().Derive(assets, allocations);
}
