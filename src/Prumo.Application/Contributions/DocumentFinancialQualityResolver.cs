using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Application.Contributions;

public static class DocumentFinancialQualityResolver
{
    public static CandidateFinancialSnapshot? Resolve(Asset asset)
    {
        var evidenceSet = MetricEvidenceResolver.Resolve(asset);
        var resolved = evidenceSet.CanonicalEvidence.ToArray();
        if (resolved.Length == 0) return null;
        var reference = resolved.Select(x => x.ReferenceDate ?? x.ReferencePeriod ?? DateOnly.MinValue).Max();
        var period = resolved.FirstOrDefault(x => (x.ReferenceDate ?? x.ReferencePeriod) == reference)?.PeriodType ?? FinancialPeriodType.PointInTime;
        decimal? Value(string metric)
        {
            var resolution = evidenceSet.Metrics.FirstOrDefault(x => string.Equals(x.Metric, metric, StringComparison.OrdinalIgnoreCase));
            return resolution?.Status == MetricEvidenceResolutionStatus.Conflict ? null : resolution?.Canonical?.Value;
        }
        var first = resolved[0];
        var source = first.SourceType == MetricEvidenceSourceType.Document ? FinancialDataSource.Derived : first.SourceReference == nameof(FinancialDataSource.Provider) ? FinancialDataSource.Provider : FinancialDataSource.Manual;
        return new(reference, period, first.ObservedAt ?? first.ExtractedAt ?? DateTimeOffset.MinValue, source,
            Value("NetIncome"), Value("Equity"), Value("TotalDebt"), Value("Cash"), Value("Roe"), Value("PriceEarnings"), Value("PriceBook"), Value("EvEbitda"), Value("DividendYield"),
            first.SourceReference, Value("Revenue"), Value("Ebitda"), Value("Roic"), Value("NetMargin"));
    }
}
