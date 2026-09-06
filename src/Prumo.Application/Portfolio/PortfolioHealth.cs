using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Domain.Rules;
using DomainPortfolio = Prumo.Domain.Entities.Portfolio;

namespace Prumo.Application.Portfolio;

public sealed record PortfolioHealthPolicy(string Version, decimal AlignedTolerancePercentagePoints, decimal SlightDeviationLimitPercentagePoints, decimal MaterialDeviationLimitPercentagePoints)
{
    public static PortfolioHealthPolicy V1 { get; } = new("1", 2m, 5m, 10m);
}

public sealed record AllocationDeviation(AssetClass AssetClass, decimal CurrentPercentage, decimal TargetPercentage, decimal DeviationPercentagePoints, decimal AbsoluteDeviationPercentagePoints, AllocationDeviationDirection Direction);
public sealed record MainDeviation(AssetClass AssetClass, decimal CurrentPercentage, decimal TargetPercentage, decimal DeviationPercentagePoints, AllocationDeviationDirection Direction);
public sealed record CapitalNeed(AssetClass AssetClass, decimal CurrentPercentage, decimal TargetPercentage, decimal DeficitPercentagePoints, int PriorityOrder);
public sealed record PortfolioHealthResult(PortfolioHealthStatus Status, IReadOnlyList<AllocationDeviation> AllocationDeviations, MainDeviation? MainDeviation, IReadOnlyList<CapitalNeed> CapitalNeeds, string PolicyVersion, DateTimeOffset GeneratedAt);

public sealed class PortfolioHealthCalculator
{
    public PortfolioHealthResult Calculate(DomainPortfolio portfolio, IReadOnlyList<TargetAllocation>? targetAllocations, PortfolioHealthPolicy? policy = null)
    {
        policy ??= PortfolioHealthPolicy.V1;
        var generatedAt = DateTimeOffset.UtcNow;
        var configuredTargets = targetAllocations?.Where(x => x.TargetPercentage > 0).ToList();
        if (configuredTargets is null || configuredTargets.Count == 0 || configuredTargets.GroupBy(x => x.AssetClass).Any(x => x.Count() > 1) || Math.Abs(configuredTargets.Sum(x => x.TargetPercentage) - 100m) > .01m)
            return new(PortfolioHealthStatus.NotConfigured, Array.Empty<AllocationDeviation>(), null, Array.Empty<CapitalNeed>(), policy.Version, generatedAt);

        var eligible = portfolio.Positions.Where(p => p.CurrentValue.HasValue).ToList();
        if (eligible.Count == 0)
            return new(PortfolioHealthStatus.EmptyPortfolio, Array.Empty<AllocationDeviation>(), null, Array.Empty<CapitalNeed>(), policy.Version, generatedAt);

        PortfolioCalculator.RecalculateWeights(eligible);
        var total = PortfolioCalculator.CalculateTotalValue(eligible);
        var currentByClass = eligible.GroupBy(p => p.Asset?.AssetClass ?? AssetClass.Other).ToDictionary(g => g.Key, g => total > 0 ? g.Sum(p => p.CurrentValue ?? 0) / total * 100m : 0m);
        var targetByClass = configuredTargets.ToDictionary(a => a.AssetClass, a => a.TargetPercentage);
        // Only evaluate classes explicitly included in the target allocation.
        // Positions from classes without a configured target must not appear as
        // if their implicit target were 0%.
        var classes = targetByClass.Keys.OrderBy(c => c.ToString(), StringComparer.Ordinal).ToList();
        var deviations = classes.Select(assetClass =>
        {
            var current = currentByClass.GetValueOrDefault(assetClass);
            var target = targetByClass.GetValueOrDefault(assetClass);
            var deviation = current - target;
            var absolute = Math.Abs(deviation);
            var direction = absolute <= policy.AlignedTolerancePercentagePoints
                ? AllocationDeviationDirection.Aligned
                : deviation < 0 ? AllocationDeviationDirection.Underweight : AllocationDeviationDirection.Overweight;
            return new AllocationDeviation(assetClass, current, target, deviation, absolute, direction);
        }).ToList();

        var main = deviations.OrderByDescending(d => d.AbsoluteDeviationPercentagePoints).ThenBy(d => d.AssetClass.ToString(), StringComparer.Ordinal).FirstOrDefault();
        var needs = deviations.Where(d => d.Direction == AllocationDeviationDirection.Underweight)
            .OrderByDescending(d => d.AbsoluteDeviationPercentagePoints).ThenBy(d => d.AssetClass.ToString(), StringComparer.Ordinal)
            .Select((d, i) => new CapitalNeed(d.AssetClass, d.CurrentPercentage, d.TargetPercentage, d.AbsoluteDeviationPercentagePoints, i + 1)).ToList();
        var maximumDeviation = main?.AbsoluteDeviationPercentagePoints ?? 0;
        var status = maximumDeviation <= policy.AlignedTolerancePercentagePoints ? PortfolioHealthStatus.Aligned
            : maximumDeviation <= policy.SlightDeviationLimitPercentagePoints ? PortfolioHealthStatus.SlightlyUnbalanced
            : maximumDeviation <= policy.MaterialDeviationLimitPercentagePoints ? PortfolioHealthStatus.Unbalanced
            : PortfolioHealthStatus.StronglyUnbalanced;
        var mainDeviation = main is null ? null : new MainDeviation(main.AssetClass, main.CurrentPercentage, main.TargetPercentage, main.DeviationPercentagePoints, main.Direction);
        return new(status, deviations, mainDeviation, needs, policy.Version, generatedAt);
    }
}
