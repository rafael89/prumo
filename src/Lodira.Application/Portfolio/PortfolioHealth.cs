using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Domain.Rules;
using DomainPortfolio = Lodira.Domain.Entities.Portfolio;

namespace Lodira.Application.Portfolio;

public sealed record PortfolioHealthPolicy(string Version, decimal AlignedTolerancePercentagePoints, decimal SlightDeviationLimitPercentagePoints, decimal MaterialDeviationLimitPercentagePoints)
{
    public static PortfolioHealthPolicy V1 { get; } = new("1", 2m, 5m, 10m);
}

public sealed record AllocationDeviation(AssetClass AssetClass, decimal CurrentPercentage, decimal TargetPercentage, decimal DeviationPercentagePoints, decimal AbsoluteDeviationPercentagePoints, AllocationDeviationDirection Direction);
public sealed record MainDeviation(AssetClass AssetClass, decimal CurrentPercentage, decimal TargetPercentage, decimal DeviationPercentagePoints, AllocationDeviationDirection Direction);
public sealed record CapitalNeed(AssetClass AssetClass, decimal CurrentPercentage, decimal TargetPercentage, decimal DeficitPercentagePoints, int PriorityOrder);
public sealed record Concentration(Guid AssetId, string Ticker, AssetClass AssetClass, decimal CurrentWeight, decimal? ConfiguredMaximumWeight, bool ExceedsConfiguredMaximum);
public sealed record PortfolioHealthResult(PortfolioHealthStatus Status, IReadOnlyList<AllocationDeviation> AllocationDeviations, MainDeviation? MainDeviation, IReadOnlyList<CapitalNeed> CapitalNeeds, IReadOnlyList<Concentration> Concentrations, string PolicyVersion, int? StrategyVersion, DateTimeOffset GeneratedAt);

public sealed class PortfolioHealthCalculator
{
    public PortfolioHealthResult Calculate(DomainPortfolio portfolio, PortfolioAllocationStrategy? strategy, PortfolioHealthPolicy? policy = null)
    {
        policy ??= PortfolioHealthPolicy.V1;
        var generatedAt = DateTimeOffset.UtcNow;
        if (strategy is null)
            return new(PortfolioHealthStatus.NotConfigured, Array.Empty<AllocationDeviation>(), null, Array.Empty<CapitalNeed>(), Array.Empty<Concentration>(), policy.Version, null, generatedAt);

        var eligible = portfolio.Positions.Where(p => p.CurrentValue.HasValue).ToList();
        if (eligible.Count == 0)
            return new(PortfolioHealthStatus.EmptyPortfolio, Array.Empty<AllocationDeviation>(), null, Array.Empty<CapitalNeed>(), Array.Empty<Concentration>(), policy.Version, strategy.Version, generatedAt);

        PortfolioCalculator.RecalculateWeights(eligible);
        var total = PortfolioCalculator.CalculateTotalValue(eligible);
        var currentByClass = eligible.GroupBy(p => p.Asset?.AssetClass ?? AssetClass.Other).ToDictionary(g => g.Key, g => total > 0 ? g.Sum(p => p.CurrentValue ?? 0) / total * 100m : 0m);
        var targetByClass = strategy.Allocations.ToDictionary(a => a.AssetClass, a => a.TargetPercentage);
        var classes = currentByClass.Keys.Union(targetByClass.Keys).Distinct().OrderBy(c => c.ToString(), StringComparer.Ordinal).ToList();
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
        var concentrations = eligible.OrderByDescending(p => p.PortfolioWeight).ThenBy(p => p.Asset?.Ticker ?? string.Empty, StringComparer.Ordinal).ThenBy(p => p.AssetId)
            .Select(p => new Concentration(p.AssetId, p.Asset?.Ticker ?? string.Empty, p.Asset?.AssetClass ?? AssetClass.Other, p.PortfolioWeight, strategy.MaximumAssetWeight, strategy.MaximumAssetWeight.HasValue && p.PortfolioWeight > strategy.MaximumAssetWeight.Value)).ToList();
        var maximumDeviation = main?.AbsoluteDeviationPercentagePoints ?? 0;
        var status = maximumDeviation <= policy.AlignedTolerancePercentagePoints ? PortfolioHealthStatus.Aligned
            : maximumDeviation <= policy.SlightDeviationLimitPercentagePoints ? PortfolioHealthStatus.SlightlyUnbalanced
            : maximumDeviation <= policy.MaterialDeviationLimitPercentagePoints ? PortfolioHealthStatus.Unbalanced
            : PortfolioHealthStatus.StronglyUnbalanced;
        var mainDeviation = main is null ? null : new MainDeviation(main.AssetClass, main.CurrentPercentage, main.TargetPercentage, main.DeviationPercentagePoints, main.Direction);
        return new(status, deviations, mainDeviation, needs, concentrations, policy.Version, strategy.Version, generatedAt);
    }
}
