using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Application.Contributions;

public enum MetricEvidenceSourceType { Document, OfficialStructured, FundamentalSnapshot, ValuationSnapshot }
public enum MetricEvidenceResolutionStatus { Resolved, Unavailable, Conflict }
public enum MetricApplicability { Available, Partial, Unavailable, Stale, NotApplicable }
public enum CandidateIntelligenceDimension { FinancialFundamental, Valuation, IncomeDistributions, Events, Assessments }

public sealed record MetricEvidence(
    string Metric,
    decimal Value,
    string Unit,
    string? Currency,
    DateOnly? ReferenceDate,
    DateOnly? ReferencePeriod,
    FinancialPeriodType? PeriodType,
    DateTimeOffset? ObservedAt,
    DateTimeOffset? ExtractedAt,
    MetricEvidenceSourceType SourceType,
    string? SourceId,
    string? SourceReference,
    decimal? Confidence)
{
    public int? SourceVersion { get; init; }
}

public sealed record MetricEvidenceResolution(
    string Metric,
    MetricEvidence? Canonical,
    IReadOnlyList<MetricEvidence> Alternatives,
    MetricEvidenceResolutionStatus Status,
    string? ConflictReason = null);

public sealed record MetricEvidenceSet(IReadOnlyList<MetricEvidenceResolution> Metrics)
{
    public IEnumerable<MetricEvidence> CanonicalEvidence => Metrics.Where(x => x.Canonical is not null).Select(x => x.Canonical!);
}

public sealed record CandidateDimensionCoverage(
    CandidateIntelligenceDimension Dimension,
    MetricApplicability Applicability,
    int ApplicableMetricCount,
    int AvailableMetricCount,
    int StaleMetricCount,
    IReadOnlyList<string> MissingMetrics,
    DateTimeOffset? LastObservedAt);

public sealed record CandidateFreshness(
    string PolicyVersion,
    IReadOnlyDictionary<string, MetricApplicability> ByMetric,
    IReadOnlyDictionary<CandidateIntelligenceDimension, MetricApplicability> ByDimension);

public static class CandidateFreshnessPolicy
{
    public const string Version = "candidate-intelligence-freshness-v1";

    public static MetricApplicability Evaluate(string metric, DateOnly? referenceDate, DateOnly? referencePeriod,
        FinancialPeriodType? periodType, DateTimeOffset? observedAt, DateTimeOffset now)
    {
        if (observedAt is null && referenceDate is null && referencePeriod is null) return MetricApplicability.Unavailable;
        var family = metric switch
        {
            "PriceEarnings" or "PriceBook" or "EvEbitda" => "MarketValuation",
            "DividendYield" => "Income",
            "NetAssetValue" or "NAVPerShare" or "Cash" or "DistributionPerShare" => "FiiMonthly",
            _ => "FinancialStatements"
        };
        var maxAge = family switch
        {
            "MarketValuation" => TimeSpan.FromDays(35),
            "Income" => TimeSpan.FromDays(120),
            "FiiMonthly" => TimeSpan.FromDays(120),
            _ => periodType == FinancialPeriodType.Annual ? TimeSpan.FromDays(550) : TimeSpan.FromDays(400)
        };
        var observedAge = observedAt.HasValue ? now - observedAt.Value : TimeSpan.Zero;
        var reference = referenceDate ?? referencePeriod;
        var referenceAge = reference.HasValue ? now.UtcDateTime.Date - reference.Value.ToDateTime(TimeOnly.MinValue) : TimeSpan.Zero;
        var age = observedAge > referenceAge ? observedAge : referenceAge;
        return age <= maxAge ? MetricApplicability.Available : MetricApplicability.Stale;
    }
}

public static class MetricEvidenceResolver
{
    private static readonly IReadOnlyDictionary<string, string> SnapshotUnits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Revenue"] = "value", ["NetIncome"] = "value", ["Equity"] = "value", ["TotalDebt"] = "value", ["Cash"] = "value", ["Ebitda"] = "value",
        ["Roe"] = "ratio", ["Roic"] = "ratio", ["NetMargin"] = "ratio", ["PriceEarnings"] = "multiple", ["PriceBook"] = "multiple", ["EvEbitda"] = "multiple", ["DividendYield"] = "ratio"
    };

    public static MetricEvidenceSet Resolve(Asset asset)
    {
        var evidence = new List<MetricEvidence>();
        foreach (var fact in asset.DocumentFinancialFacts)
        {
            var reference = fact.ReferencePeriod;
            evidence.Add(new(fact.Metric.ToString(), fact.Value, fact.Unit, fact.Currency, reference, reference, fact.PeriodType,
                fact.Document?.PublishedAt, fact.ExtractedAt, MetricEvidenceSourceType.Document, fact.DocumentId.ToString(), fact.Evidence, fact.Confidence));
        }
        foreach (var snapshot in asset.FundamentalSnapshots)
            AddFundamental(evidence, snapshot);
        foreach (var snapshot in asset.ValuationSnapshots)
            AddValuation(evidence, snapshot);
        foreach (var observation in asset.CvmFiiMonthlyObservations)
            AddCvmFii(evidence, observation);

        var currentOfficialIds = asset.CvmFiiMonthlyObservations
            .GroupBy(x => (Dataset: x.SourceReference.Split('#')[0], x.ReferenceDate))
            .SelectMany(group => group.Where(x => x.SourceVersion == group.Max(y => y.SourceVersion)))
            .Select(x => x.Id.ToString()).ToHashSet();
        return new(evidence.GroupBy(x => x.Metric, StringComparer.OrdinalIgnoreCase)
            .Select(group => ResolveMetric(group, currentOfficialIds)).OrderBy(x => x.Metric, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static MetricEvidenceResolution ResolveMetric(IGrouping<string, MetricEvidence> group, IReadOnlySet<string> currentOfficialIds)
    {
        var ordered = group.OrderByDescending(x => x.ReferencePeriod ?? x.ReferenceDate ?? DateOnly.MinValue)
            .ThenBy(x => x.PeriodType switch { FinancialPeriodType.Annual => 0, FinancialPeriodType.Ttm => 1, FinancialPeriodType.Quarterly => 2, FinancialPeriodType.PointInTime => 3, _ => 4 })
            .ThenByDescending(x => x.SourceVersion)
            .ThenByDescending(x => x.ExtractedAt ?? x.ObservedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(x => x.Confidence ?? 0).ToArray();
        if (ordered.Length == 0) return new(group.Key, null, Array.Empty<MetricEvidence>(), MetricEvidenceResolutionStatus.Unavailable);
        var selected = ordered.FirstOrDefault(x => x.SourceType != MetricEvidenceSourceType.OfficialStructured
            || currentOfficialIds.Contains(x.SourceId!));
        if (selected is null) return new(group.Key, null, ordered, MetricEvidenceResolutionStatus.Unavailable);
        var sameObservation = ordered.Where(x => x.ReferenceDate == selected.ReferenceDate && x.ReferencePeriod == selected.ReferencePeriod && x.PeriodType == selected.PeriodType).ToArray();
        var differentValues = sameObservation.Select(x => (x.Value, x.Unit, x.Currency)).Distinct().Count() > 1;
        var sameOfficialDataset = sameObservation.All(x => x.SourceType == MetricEvidenceSourceType.OfficialStructured)
            && sameObservation.Select(x => x.SourceReference?.Split('#')[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
        var conflict = differentValues && !sameOfficialDataset;
        return new(group.Key, selected, ordered.Where(x => x != selected).ToArray(), conflict ? MetricEvidenceResolutionStatus.Conflict : MetricEvidenceResolutionStatus.Resolved,
            conflict ? "Existem valores diferentes para a mesma métrica, período e tipo de período." : null);
    }

    private static void AddFundamental(List<MetricEvidence> target, AssetFundamentalSnapshot snapshot)
    {
        Add(target, "Revenue", snapshot.Revenue, snapshot.ReferenceDate, snapshot.PeriodType, snapshot.ObservedAt, snapshot.Source, snapshot.SourceReference, snapshot.Id);
        Add(target, "NetIncome", snapshot.NetIncome, snapshot.ReferenceDate, snapshot.PeriodType, snapshot.ObservedAt, snapshot.Source, snapshot.SourceReference, snapshot.Id);
        Add(target, "Equity", snapshot.Equity, snapshot.ReferenceDate, snapshot.PeriodType, snapshot.ObservedAt, snapshot.Source, snapshot.SourceReference, snapshot.Id);
        Add(target, "TotalDebt", snapshot.TotalDebt, snapshot.ReferenceDate, snapshot.PeriodType, snapshot.ObservedAt, snapshot.Source, snapshot.SourceReference, snapshot.Id);
        Add(target, "Cash", snapshot.Cash, snapshot.ReferenceDate, snapshot.PeriodType, snapshot.ObservedAt, snapshot.Source, snapshot.SourceReference, snapshot.Id);
        Add(target, "Ebitda", snapshot.Ebitda, snapshot.ReferenceDate, snapshot.PeriodType, snapshot.ObservedAt, snapshot.Source, snapshot.SourceReference, snapshot.Id);
        Add(target, "Roe", snapshot.Roe, snapshot.ReferenceDate, snapshot.PeriodType, snapshot.ObservedAt, snapshot.Source, snapshot.SourceReference, snapshot.Id);
        Add(target, "Roic", snapshot.Roic, snapshot.ReferenceDate, snapshot.PeriodType, snapshot.ObservedAt, snapshot.Source, snapshot.SourceReference, snapshot.Id);
        Add(target, "NetMargin", snapshot.NetMargin, snapshot.ReferenceDate, snapshot.PeriodType, snapshot.ObservedAt, snapshot.Source, snapshot.SourceReference, snapshot.Id);
    }

    private static void AddValuation(List<MetricEvidence> target, AssetValuationSnapshot snapshot)
    {
        Add(target, "PriceEarnings", snapshot.PriceEarnings, snapshot.ReferenceDate, FinancialPeriodType.PointInTime, snapshot.ObservedAt, snapshot.Source, snapshot.SourceReference, snapshot.Id);
        Add(target, "PriceBook", snapshot.PriceBook, snapshot.ReferenceDate, FinancialPeriodType.PointInTime, snapshot.ObservedAt, snapshot.Source, snapshot.SourceReference, snapshot.Id);
        Add(target, "EvEbitda", snapshot.EvEbitda, snapshot.ReferenceDate, FinancialPeriodType.PointInTime, snapshot.ObservedAt, snapshot.Source, snapshot.SourceReference, snapshot.Id);
        Add(target, "DividendYield", snapshot.DividendYield, snapshot.ReferenceDate, FinancialPeriodType.PointInTime, snapshot.ObservedAt, snapshot.Source, snapshot.SourceReference, snapshot.Id);
    }

    private static void AddCvmFii(List<MetricEvidence> target, CvmFiiMonthlyObservation observation)
    {
        AddStructured(target, "NetAssetValue", observation.NetAssetValue, observation, "currency", "BRL");
        AddStructured(target, "NAVPerShare", observation.NavPerShare, observation, "currency", "BRL");
        AddStructured(target, "Cash", observation.Cash, observation, "currency", "BRL");
    }

    private static void AddStructured(List<MetricEvidence> target, string metric, decimal? value, CvmFiiMonthlyObservation observation, string unit, string currency)
    {
        if (value is null) return;
        target.Add(new(metric, value.Value, unit, currency, observation.ReferenceDate, observation.ReferenceDate,
            FinancialPeriodType.PointInTime, observation.ObservedAt, observation.ObservedAt,
            MetricEvidenceSourceType.OfficialStructured, observation.Id.ToString(), observation.SourceReference, null)
            { SourceVersion = observation.SourceVersion });
    }

    private static void Add(List<MetricEvidence> target, string metric, decimal? value, DateOnly date, FinancialPeriodType period, DateTimeOffset observedAt, FinancialDataSource source, string? sourceReference, Guid sourceId)
    {
        if (value is null) return;
        target.Add(new(metric, value.Value, SnapshotUnits[metric], null, date, date, period, observedAt, null,
            metric is "PriceEarnings" or "PriceBook" or "EvEbitda" or "DividendYield" ? MetricEvidenceSourceType.ValuationSnapshot : MetricEvidenceSourceType.FundamentalSnapshot,
            sourceId.ToString(), sourceReference ?? source.ToString(), null));
    }
}

public static class MetricApplicabilityResolver
{
    private static readonly string[] StockMetrics = { "Revenue", "NetIncome", "Equity", "TotalDebt", "Cash", "Ebitda", "Roe", "Roic", "NetMargin" };
    private static readonly string[] ValuationMetrics = { "PriceEarnings", "PriceBook", "EvEbitda" };
    private static readonly string[] FiiFundFinancialMetrics = { "NetAssetValue", "NAVPerShare", "Cash" };

    public static MetricApplicability For(string metric, AssetType assetType, FundType fundType)
    {
        if (assetType == AssetType.Stock)
            return FiiFundFinancialMetrics.Contains(metric, StringComparer.OrdinalIgnoreCase) || metric.Equals("DistributionPerShare", StringComparison.OrdinalIgnoreCase)
                ? MetricApplicability.NotApplicable
                : MetricApplicability.Available;
        if (assetType == AssetType.Fii)
        {
            if (StockMetrics.Contains(metric, StringComparer.OrdinalIgnoreCase)) return MetricApplicability.NotApplicable;
            if (metric == "DividendYield" || ValuationMetrics.Contains(metric, StringComparer.OrdinalIgnoreCase)) return MetricApplicability.NotApplicable;
            if (FiiFundFinancialMetrics.Contains(metric, StringComparer.OrdinalIgnoreCase))
                return fundType == FundType.Brick ? MetricApplicability.Available : MetricApplicability.NotApplicable;
        }
        return MetricApplicability.Unavailable;
    }

    public static CandidateDimensionCoverage Dimension(CandidateIntelligenceDimension dimension, AssetType assetType, FundType fundType, MetricEvidenceSet evidence, DateTimeOffset now)
    {
        var metrics = dimension switch
        {
            CandidateIntelligenceDimension.FinancialFundamental => assetType == AssetType.Fii
                ? FiiFundFinancialMetrics
                : StockMetrics,
            CandidateIntelligenceDimension.Valuation => ValuationMetrics,
            CandidateIntelligenceDimension.IncomeDistributions => assetType == AssetType.Fii && fundType == FundType.Brick
                ? new[] { "DistributionPerShare" }
                : new[] { "DividendYield" },
            _ => Array.Empty<string>()
        };
        if (metrics.Length == 0) return new(dimension, MetricApplicability.Unavailable, 0, 0, 0, Array.Empty<string>(), null);
        var applicable = metrics.Where(x => For(x, assetType, fundType) != MetricApplicability.NotApplicable).ToArray();
        if (applicable.Length == 0) return new(dimension, MetricApplicability.NotApplicable, 0, 0, 0, Array.Empty<string>(), null);
        var byName = evidence.Metrics.ToDictionary(x => x.Metric, StringComparer.OrdinalIgnoreCase);
        var states = applicable.Select(metric =>
        {
            if (!byName.TryGetValue(metric, out var resolution) || resolution.Canonical is null) return (metric, state: MetricApplicability.Unavailable, observed: (DateTimeOffset?)null);
            var state = CandidateFreshnessPolicy.Evaluate(metric, resolution.Canonical.ReferenceDate, resolution.Canonical.ReferencePeriod, resolution.Canonical.PeriodType, resolution.Canonical.ObservedAt, now);
            return (metric, state, observed: resolution.Canonical.ObservedAt ?? resolution.Canonical.ExtractedAt);
        }).ToArray();
        var missing = states.Where(x => x.state is MetricApplicability.Unavailable or MetricApplicability.Stale).Select(x => x.metric).ToArray();
        var available = states.Count(x => x.state == MetricApplicability.Available);
        var stale = states.Count(x => x.state == MetricApplicability.Stale);
        var status = available == states.Length ? MetricApplicability.Available : available > 0 ? MetricApplicability.Partial : stale > 0 ? MetricApplicability.Stale : MetricApplicability.Unavailable;
        return new(dimension, status, states.Length, available, stale, missing, states.Select(x => x.observed).Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty().Max());
    }
}
