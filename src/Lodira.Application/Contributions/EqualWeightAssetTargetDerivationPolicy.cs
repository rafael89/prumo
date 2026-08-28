using Lodira.Domain.Enums;

namespace Lodira.Application.Contributions;

public sealed record AssetForTarget(Guid AssetId, string Ticker, AssetClass AssetClass, decimal CurrentValue);
public sealed record DerivedAssetTarget(Guid AssetId, string Ticker, AssetClass AssetClass, decimal TargetPercentage, decimal CurrentValue);

public interface IAssetTargetDerivationPolicy
{
    IReadOnlyList<DerivedAssetTarget> Derive(IEnumerable<AssetForTarget> assets, IEnumerable<(AssetClass AssetClass, decimal TargetPercentage)> allocations);
}

public sealed class EqualWeightAssetTargetDerivationPolicy : IAssetTargetDerivationPolicy
{
    public IReadOnlyList<DerivedAssetTarget> Derive(IEnumerable<AssetForTarget> assets, IEnumerable<(AssetClass AssetClass, decimal TargetPercentage)> allocations)
    {
        var assetList = assets.Where(a => a.CurrentValue >= 0).OrderBy(a => a.AssetId).ToList();
        var targets = allocations.ToDictionary(a => a.AssetClass, a => a.TargetPercentage);
        var result = new List<DerivedAssetTarget>();
        foreach (var group in assetList.GroupBy(a => a.AssetClass).OrderBy(g => g.Key))
        {
            var target = targets.GetValueOrDefault(group.Key);
            var members = group.OrderBy(a => a.AssetId).ToList();
            var equalWeight = members.Count == 0 ? 0m : target / members.Count;
            var assigned = 0m;
            for (var index = 0; index < members.Count; index++)
            {
                var value = index == members.Count - 1 ? target - assigned : equalWeight;
                assigned += value;
                var asset = members[index];
                result.Add(new DerivedAssetTarget(asset.AssetId, asset.Ticker, asset.AssetClass, value, asset.CurrentValue));
            }
        }
        return result.OrderBy(item => item.AssetId).ToList();
    }
}
