using Prumo.Application.Abstractions;

namespace Prumo.Application.Contributions;

public static class ContributionProjectionEvaluator
{
    public static decimal AggregateClassDeviation(IReadOnlyList<ContributionCandidate> candidates, IEnumerable<CandidateAllocation> allocations)
    {
        var allocationByClass = allocations.GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.Sum(y => y.Amount));
        var currentTotal = candidates.Sum(x => x.CurrentValue);
        var allocatedTotal = allocationByClass.Values.Sum();
        if (currentTotal + allocatedTotal <= 0) return decimal.MaxValue;
        return candidates.GroupBy(x => x.AssetClass).Sum(group =>
        {
            var first = group.First();
            var added = group.Sum(x => allocationByClass.GetValueOrDefault(x.CandidateKey));
            var projectedWeight = (group.Sum(x => x.CurrentValue) + added) / (currentTotal + allocatedTotal) * 100m;
            return Math.Abs(projectedWeight - first.TargetClassWeight);
        });
    }

    public static decimal AggregateClassDeviation(
        IReadOnlyList<ContributionCandidate> candidates,
        IEnumerable<(Guid AssetId, decimal Amount)> allocations)
        => AggregateClassDeviation(candidates, allocations.Select(x => new CandidateAllocation(CandidateIdentity.Known(x.AssetId), x.Amount)));
}

public sealed record CandidateAllocation(CandidateIdentity Identity, decimal Amount)
{
    public CandidateKey Key => Identity.Key;
    public Guid? AssetId => Identity.AssetId;
    public CandidateAllocation(Guid assetId, decimal amount) : this(CandidateIdentity.Known(assetId), amount) { }
}
