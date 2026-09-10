using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Application.Contributions;

public static class DocumentFinancialQualityResolver
{
    public static CandidateFinancialSnapshot? Resolve(Asset asset)
    {
        var existing = asset.FundamentalSnapshots.OrderByDescending(x => x.ReferenceDate).ThenByDescending(x => x.ObservedAt).FirstOrDefault();
        var valuation = asset.ValuationSnapshots.OrderByDescending(x => x.ReferenceDate).ThenByDescending(x => x.ObservedAt).FirstOrDefault();
        var facts = asset.DocumentFinancialFacts.GroupBy(x => x.Metric).ToDictionary(x => x.Key, ResolveMetric);
        if (existing is null && valuation is null && facts.Count == 0) return null;
        var usable = facts.Values.Where(x => x is not null).Select(x => x!).ToArray();
        var reference = usable.Select(x => x.ReferencePeriod).DefaultIfEmpty(existing?.ReferenceDate ?? DateOnly.MinValue).Max();
        var period = usable.FirstOrDefault(x => x.ReferencePeriod == reference)?.PeriodType ?? existing?.PeriodType ?? FinancialPeriodType.PointInTime;
        decimal? Value(FinancialFactMetric metric, decimal? fallback) => facts.TryGetValue(metric, out var fact) ? fact?.Value : fallback;
        var observedAt = usable.FirstOrDefault()?.ExtractedAt ?? existing?.ObservedAt ?? valuation?.ObservedAt ?? DateTimeOffset.MinValue;
        return new(reference, period, observedAt, usable.Length > 0 || existing is null ? FinancialDataSource.Derived : existing.Source,
            Value(FinancialFactMetric.NetIncome, existing?.NetIncome), Value(FinancialFactMetric.Equity, existing?.Equity), Value(FinancialFactMetric.TotalDebt, existing?.TotalDebt), Value(FinancialFactMetric.Cash, existing?.Cash), Value(FinancialFactMetric.Roe, existing?.Roe), valuation?.PriceEarnings, valuation?.PriceBook, valuation?.EvEbitda, valuation?.DividendYield, usable.FirstOrDefault() is { } fact ? $"Document:{fact.DocumentId}" : existing?.SourceReference ?? valuation?.SourceReference);
    }

    private static ResolvedFact? ResolveMetric(IEnumerable<DocumentFinancialFact> candidates)
    {
        var ordered = candidates.OrderByDescending(x => x.ReferencePeriod).ThenBy(x => x.PeriodType switch { FinancialPeriodType.Annual => 0, FinancialPeriodType.Ttm => 1, FinancialPeriodType.Quarterly => 2, _ => 3 }).ThenByDescending(x => x.ExtractedAt).ToArray();
        var samePeriod = ordered.Where(x => x.ReferencePeriod == ordered[0].ReferencePeriod && x.PeriodType == ordered[0].PeriodType).ToArray();
        return samePeriod.Select(x => x.Value).Distinct().Count() == 1 ? new(samePeriod[0].DocumentId, samePeriod[0].ReferencePeriod, samePeriod[0].PeriodType, samePeriod[0].Value, samePeriod[0].ExtractedAt, samePeriod[0].Evidence) : null;
    }

    private sealed record ResolvedFact(Guid DocumentId, DateOnly ReferencePeriod, FinancialPeriodType PeriodType, decimal Value, DateTimeOffset ExtractedAt, string Evidence);
}
