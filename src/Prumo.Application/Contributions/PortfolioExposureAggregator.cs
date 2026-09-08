using Prumo.Domain.Enums;
using Prumo.Application.Abstractions;

namespace Prumo.Application.Contributions;

public sealed record PortfolioExposureBucket(string Key, decimal PortfolioPercentage);
public sealed record PortfolioExposureDimensionSummary(string Dimension, decimal KnownPortfolioPercentage, decimal UnknownPortfolioPercentage, ExposureCoverage Coverage, IReadOnlyList<PortfolioExposureBucket> Buckets);
public sealed record PortfolioExposureSummary(decimal InvestedPortfolioValue, PortfolioExposureDimensionSummary Sector, PortfolioExposureDimensionSummary Geography, PortfolioExposureDimensionSummary Currency);
public sealed record PortfolioExposureImpact(string Dimension, string Key, decimal BeforePercentage, decimal AfterPercentage);
public sealed record MultidimensionalPortfolioNeed(string Type, string Dimension, string? Key, decimal Severity, string Confidence, ExposureCoverage Coverage, IReadOnlyList<string> Reasons);
public sealed record PortfolioStructuralNeed(
    string Type,
    string Dimension,
    string Key,
    decimal Severity,
    IReadOnlyList<CandidateKey> CandidateKeys,
    string Explanation);

public static class PortfolioExposureAggregator
{
    public static PortfolioExposureSummary Aggregate(IReadOnlyList<ContributionCandidate> candidates, IEnumerable<CandidateAllocation> allocations)
    {
        var additions = allocations.GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.Sum(y => y.Amount));
        var projected = candidates.Select(candidate => candidate with { CurrentValue = candidate.CurrentValue + additions.GetValueOrDefault(candidate.CandidateKey) }).ToArray();
        return Aggregate(projected);
    }

    public static PortfolioExposureSummary Aggregate(IReadOnlyList<ContributionCandidate> candidates)
    {
        var total = candidates.Where(x => x.CurrentValue > 0).Sum(x => x.CurrentValue);
        return new(total, AggregateSector(candidates, total), AggregateGeography(candidates, total), AggregateCurrency(candidates, total));
    }

    public static PortfolioExposureSummary Aggregate(IReadOnlyList<ContributionCandidate> candidates, IEnumerable<(Guid AssetId, decimal Amount)> allocations)
    {
        var byId = allocations.GroupBy(x => x.AssetId).ToDictionary(x => x.Key, x => x.Sum(y => y.Amount));
        var projected = candidates.Select(candidate => candidate with { CurrentValue = candidate.CurrentValue + byId.GetValueOrDefault(candidate.AssetId) }).ToArray();
        return Aggregate(projected);
    }

    public static IReadOnlyList<PortfolioExposureImpact> Compare(PortfolioExposureSummary before, PortfolioExposureSummary after)
        => new[] { (before.Sector, after.Sector), (before.Geography, after.Geography), (before.Currency, after.Currency) }
            .SelectMany(pair => pair.Item2.Buckets.Select(bucket => new PortfolioExposureImpact(pair.Item2.Dimension, bucket.Key, pair.Item1.Buckets.FirstOrDefault(x => x.Key == bucket.Key)?.PortfolioPercentage ?? 0, bucket.PortfolioPercentage)))
            .Where(x => Math.Abs(x.AfterPercentage - x.BeforePercentage) > 0.01m)
            .ToArray();

    private static PortfolioExposureDimensionSummary AggregateSector(IReadOnlyList<ContributionCandidate> candidates, decimal total)
    {
        var known = candidates.Where(x => x.CurrentValue > 0 && x.SectorCategory.HasValue).GroupBy(x => x.SectorCategory!.Value.ToString()).ToDictionary(x => x.Key, x => x.Sum(y => y.CurrentValue));
        return Summary("Sector", known, total);
    }

    private static PortfolioExposureDimensionSummary AggregateGeography(IReadOnlyList<ContributionCandidate> candidates, decimal total)
        => Summary("Geography", Weighted(candidates, x => x.GeographicExposures?.Select(e => (e.CountryCode, e.Percentage)).ToArray() ?? Array.Empty<(string, decimal?)>()), total);

    private static PortfolioExposureDimensionSummary AggregateCurrency(IReadOnlyList<ContributionCandidate> candidates, decimal total)
        => Summary("Currency", Weighted(candidates, x => x.CurrencyExposures?.Select(e => (e.CurrencyCode, e.Percentage)).ToArray() ?? Array.Empty<(string, decimal?)>()), total);

    private static Dictionary<string, decimal> Weighted(IReadOnlyList<ContributionCandidate> candidates, Func<ContributionCandidate, IEnumerable<(string Key, decimal? Percentage)>> exposures)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates.Where(x => x.CurrentValue > 0))
        {
            var known = exposures(candidate).Where(x => x.Percentage is > 0).ToArray();
            foreach (var exposure in known) result[exposure.Key] = result.GetValueOrDefault(exposure.Key) + candidate.CurrentValue * Math.Min(exposure.Percentage!.Value, 100m) / 100m;
        }
        return result;
    }

    private static PortfolioExposureDimensionSummary Summary(string dimension, Dictionary<string, decimal> knownValues, decimal total)
    {
        var known = total <= 0 ? 0 : knownValues.Values.Sum() / total * 100m;
        known = Math.Clamp(known, 0, 100);
        var unknown = 100m - known;
        var coverage = known <= 0.01m ? ExposureCoverage.Unavailable : unknown <= 0.01m ? ExposureCoverage.Complete : ExposureCoverage.Partial;
        var buckets = knownValues.Select(x => new PortfolioExposureBucket(x.Key, total <= 0 ? 0 : x.Value / total * 100m)).OrderByDescending(x => x.PortfolioPercentage).ThenBy(x => x.Key, StringComparer.Ordinal).ToArray();
        return new(dimension, known, unknown, coverage, buckets);
    }

    public static IReadOnlyList<MultidimensionalPortfolioNeed> DetectNeeds(IReadOnlyList<ContributionCandidate> candidates, PortfolioExposureSummary exposure)
    {
        var needs = new List<MultidimensionalPortfolioNeed>();
        var total = exposure.InvestedPortfolioValue;
        if (total > 0)
        {
            var concentrated = candidates.Where(x => x.CurrentValue / total * 100m >= 20m).OrderByDescending(x => x.CurrentValue).ThenBy(x => x.Ticker, StringComparer.Ordinal).FirstOrDefault();
            if (concentrated is not null) needs.Add(new("SingleAssetConcentration", "Asset", concentrated.Ticker, concentrated.CurrentValue / total * 100m, "High", ExposureCoverage.Complete, new[] { $"{concentrated.Ticker} representa {concentrated.CurrentValue / total * 100m:0.##}% da carteira; novos aportes não devem aumentar essa concentração sem justificativa explícita." }));
        }
        AddDominantNeed(needs, exposure.Sector, "SectorConcentration");
        AddDominantNeed(needs, exposure.Geography, "GeographicConcentration");
        AddDominantNeed(needs, exposure.Currency, "CurrencyConcentration");
        return needs;
    }

    public static IReadOnlyList<PortfolioStructuralNeed> DetectStructuralNeeds(
        IReadOnlyList<ContributionCandidate> candidates,
        PortfolioExposureSummary exposure)
    {
        var result = new List<PortfolioStructuralNeed>();
        foreach (var need in DetectNeeds(candidates, exposure).Where(x => x.Key is not null && x.Coverage == ExposureCoverage.Complete))
        {
            var candidatesThatReduceConcentration = candidates
                .Where(candidate => !candidate.HasBlock && ReducesDominantExposure(candidate, need))
                .OrderByDescending(candidate => candidate.DecisionScore)
                .ThenBy(candidate => candidate.Ticker, StringComparer.Ordinal)
                .Select(candidate => candidate.CandidateKey)
                .ToArray();
            if (candidatesThatReduceConcentration.Length == 0) continue;

            result.Add(new PortfolioStructuralNeed(
                need.Type,
                need.Dimension,
                need.Key!,
                need.Severity,
                candidatesThatReduceConcentration,
                $"A carteira tem concentração estrutural em {need.Key} ({need.Severity:0.##}%), e estes candidatos reduzem essa exposição conhecida."));
        }
        return result;
    }

    private static bool ReducesDominantExposure(ContributionCandidate candidate, MultidimensionalPortfolioNeed need)
    {
        if (need.Dimension == "Sector")
            return !string.Equals(candidate.SectorCategory?.ToString(), need.Key, StringComparison.OrdinalIgnoreCase);
        if (need.Dimension == "Geography")
            return candidate.GeographicExposures?.Any(x => string.Equals(x.CountryCode, need.Key, StringComparison.OrdinalIgnoreCase) && x.Percentage is > 50m) != true;
        if (need.Dimension == "Currency")
            return candidate.CurrencyExposures?.Any(x => string.Equals(x.CurrencyCode, need.Key, StringComparison.OrdinalIgnoreCase) && x.Percentage is > 50m) != true;
        return false;
    }

    private static void AddDominantNeed(List<MultidimensionalPortfolioNeed> needs, PortfolioExposureDimensionSummary dimension, string type)
    {
        if (dimension.Coverage == ExposureCoverage.Unavailable || dimension.KnownPortfolioPercentage < 80m || dimension.Buckets.Count == 0) return;
        var dominant = dimension.Buckets[0];
        if (dominant.PortfolioPercentage < 40m) return;
        needs.Add(new(type, dimension.Dimension, dominant.Key, dominant.PortfolioPercentage, "Medium", dimension.Coverage, new[] { $"Entre os {dimension.KnownPortfolioPercentage:0.##}% da carteira com {dimension.Dimension.ToLowerInvariant()} conhecida, {dominant.Key} representa {dominant.PortfolioPercentage / dimension.KnownPortfolioPercentage * 100m:0.##}%." }));
    }
}
