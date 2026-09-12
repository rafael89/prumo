using Prumo.Application.Abstractions;
using Prumo.Domain.Enums;

namespace Prumo.Application.Documents;

public static class DocumentFinancialFactNormalizer
{
    public static FinancialFactNormalizationResult NormalizeWithReason(AiFinancialFactPayload payload)
    {
        if (payload is null) return Rejected("MissingFact");
        if (string.IsNullOrWhiteSpace(payload.Metric) || !Enum.TryParse<FinancialFactMetric>(payload.Metric, true, out var metric)) return Rejected("InvalidMetric");
        if (!payload.Value.HasValue) return Rejected("InvalidValue");
        if (string.IsNullOrWhiteSpace(payload.ReferencePeriod) || !DateOnly.TryParseExact(payload.ReferencePeriod, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var period)) return Rejected("InvalidReferencePeriod");
        if (string.IsNullOrWhiteSpace(payload.PeriodType) || !Enum.TryParse<FinancialPeriodType>(payload.PeriodType, true, out var periodType)) return Rejected("InvalidPeriodType");
        if (string.IsNullOrWhiteSpace(payload.Unit)) return Rejected("MissingUnit");
        if (string.IsNullOrWhiteSpace(payload.Evidence)) return Rejected("MissingEvidence");
        if (!payload.Confidence.HasValue || payload.Confidence is < 0 or > 1) return Rejected("InvalidConfidence");

        var unit = payload.Unit.Trim().ToLowerInvariant();
        if (metric is FinancialFactMetric.Roe or FinancialFactMetric.Roic or FinancialFactMetric.NetMargin)
        {
            if (unit == "percent") return Accepted(new(metric, period, periodType, payload.Value.Value / 100m, payload.Currency, "ratio", payload.Evidence.Trim(), payload.Confidence.Value));
            if (unit == "ratio") return Accepted(new(metric, period, periodType, payload.Value.Value, payload.Currency, "ratio", payload.Evidence.Trim(), payload.Confidence.Value));
            return Rejected("InvalidUnitForMetric");
        }

        if (string.IsNullOrWhiteSpace(payload.Currency)) return Rejected("MissingCurrency");
        var multiplier = unit switch { "currency" => 1m, "currency_thousand" => 1_000m, "currency_million" => 1_000_000m, "currency_billion" => 1_000_000_000m, _ => 0m };
        return multiplier == 0
            ? Rejected("InvalidUnitForMetric")
            : Accepted(new(metric, period, periodType, payload.Value.Value * multiplier, payload.Currency, "currency", payload.Evidence.Trim(), payload.Confidence.Value));
    }

    public static AiFinancialFactResult? Normalize(AiFinancialFactPayload payload)
    {
        return NormalizeWithReason(payload).Fact;
    }

    private static FinancialFactNormalizationResult Accepted(AiFinancialFactResult fact) => new(fact, null);
    private static FinancialFactNormalizationResult Rejected(string reason) => new(null, reason);
}

public sealed record FinancialFactNormalizationResult(AiFinancialFactResult? Fact, string? RejectionReason)
{
    public bool IsAccepted => Fact is not null;
}
