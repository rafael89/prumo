namespace Lodira.Application.Contributions;

public static class ContributionProjectionEvaluator
{
    public static decimal AggregateClassDeviation(
        IReadOnlyList<ContributionCandidate> candidates,
        IEnumerable<(Guid AssetId, decimal Amount)> allocations)
    {
        var allocationList = allocations.ToArray();
        var candidateById = candidates.ToDictionary(candidate => candidate.AssetId);
        var allocationByClass = allocationList
            .Where(allocation => candidateById.ContainsKey(allocation.AssetId))
            .GroupBy(allocation => candidateById[allocation.AssetId].AssetClass)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));
        var currentTotal = candidates.Sum(candidate => candidate.CurrentValue);
        var allocatedTotal = allocationList.Sum(allocation => allocation.Amount);
        if (currentTotal + allocatedTotal <= 0) return decimal.MaxValue;

        return candidates.GroupBy(candidate => candidate.AssetClass).Sum(group =>
        {
            var first = group.First();
            var projectedWeight = (group.Sum(candidate => candidate.CurrentValue) + allocationByClass.GetValueOrDefault(group.Key))
                / (currentTotal + allocatedTotal) * 100m;
            return Math.Abs(projectedWeight - first.TargetClassWeight);
        });
    }
}
