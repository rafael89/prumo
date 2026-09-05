using Prumo.Application.Abstractions;
using Prumo.Domain.Enums;

namespace Prumo.Application.Documents;

public static class DocumentFinancialFactNormalizer
{
    public static AiFinancialFactResult? Normalize(AiFinancialFactPayload payload)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.Metric) || !Enum.TryParse<FinancialFactMetric>(payload.Metric, true, out var metric) || !payload.Value.HasValue || string.IsNullOrWhiteSpace(payload.ReferencePeriod) || !DateOnly.TryParseExact(payload.ReferencePeriod, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var period) || string.IsNullOrWhiteSpace(payload.PeriodType) || !Enum.TryParse<FinancialPeriodType>(payload.PeriodType, true, out var periodType) || string.IsNullOrWhiteSpace(payload.Unit) || string.IsNullOrWhiteSpace(payload.Evidence) || !payload.Confidence.HasValue || payload.Confidence is < 0 or > 1) return null;
        var unit = payload.Unit.Trim().ToLowerInvariant();
        if (metric is FinancialFactMetric.Roe or FinancialFactMetric.Roic or FinancialFactMetric.NetMargin)
        {
            if (unit == "percent") return new(metric, period, periodType, payload.Value.Value / 100m, payload.Currency, "ratio", payload.Evidence.Trim(), payload.Confidence.Value);
            if (unit == "ratio") return new(metric, period, periodType, payload.Value.Value, payload.Currency, "ratio", payload.Evidence.Trim(), payload.Confidence.Value);
            return null;
        }
        if (string.IsNullOrWhiteSpace(payload.Currency)) return null;
        var multiplier = unit switch { "currency" => 1m, "currency_thousand" => 1_000m, "currency_million" => 1_000_000m, "currency_billion" => 1_000_000_000m, _ => 0m };
        return multiplier == 0 ? null : new(metric, period, periodType, payload.Value.Value * multiplier, payload.Currency, "currency", payload.Evidence.Trim(), payload.Confidence.Value);
    }
}
