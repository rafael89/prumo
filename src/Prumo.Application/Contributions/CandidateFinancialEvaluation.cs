using Prumo.Domain.Enums;

namespace Prumo.Application.Contributions;

public enum FinancialEvidenceCoverage { Unavailable, Partial, Complete }
public enum FinancialEvidenceFreshness { Unavailable, Fresh, Stale }
public sealed record CandidateFinancialSnapshot(
    DateOnly ReferenceDate,
    FinancialPeriodType PeriodType,
    DateTimeOffset ObservedAt,
    FinancialDataSource Source,
    decimal? NetIncome = null,
    decimal? Equity = null,
    decimal? TotalDebt = null,
    decimal? Cash = null,
    decimal? Roe = null,
    decimal? PriceEarnings = null,
    decimal? PriceBook = null,
    decimal? EvEbitda = null,
    decimal? DividendYield = null,
    string? SourceReference = null,
    decimal? Revenue = null,
    decimal? Ebitda = null,
    decimal? Roic = null,
    decimal? NetMargin = null);

public sealed record FinancialMetricEvidence(string Metric, decimal Value, string Semantics);
public sealed record CandidateQualityEvaluation(string Applicability, FinancialEvidenceCoverage Coverage, FinancialEvidenceFreshness Freshness, string Category, IReadOnlyList<string> Signals, IReadOnlyList<string> Concerns, IReadOnlyList<FinancialMetricEvidence> Evidence, IReadOnlyList<string> MissingData);
public sealed record CandidateValuationEvaluation(string Applicability, FinancialEvidenceCoverage Coverage, FinancialEvidenceFreshness Freshness, string Assessment, IReadOnlyList<FinancialMetricEvidence> Metrics, IReadOnlyList<string> MissingData);
public sealed record CandidateFinancialEvaluation(Guid AssetId, CandidateQualityEvaluation Quality, CandidateValuationEvaluation Valuation);

public static class CandidateFinancialEvaluator
{
    public static CandidateFinancialEvaluation Evaluate(Guid assetId, AssetType assetType, CandidateFinancialSnapshot? snapshot, DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.UtcNow;
        return new(assetId, Quality(assetType, snapshot, reference), Valuation(assetType, snapshot, reference));
    }

    private static CandidateQualityEvaluation Quality(AssetType assetType, CandidateFinancialSnapshot? snapshot, DateTimeOffset now)
    {
        if (assetType != AssetType.Stock) return new("NotApplicable", FinancialEvidenceCoverage.Unavailable, FinancialEvidenceFreshness.Unavailable, "NotApplicable", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<FinancialMetricEvidence>(), new[] { "Métricas de companhia operacional não são aplicadas a este tipo de ativo." });
        if (snapshot is null) return new("Applicable", FinancialEvidenceCoverage.Unavailable, FinancialEvidenceFreshness.Unavailable, "Unavailable", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<FinancialMetricEvidence>(), new[] { "Fundamentos não disponíveis." });
        var fresh = now - snapshot.ObservedAt <= TimeSpan.FromDays(400);
        var evidence = new List<FinancialMetricEvidence>();
        var signals = new List<string>();
        var concerns = new List<string>();
        if (snapshot.NetIncome is { } income)
        {
            evidence.Add(new("NetIncome", income, income > 0 ? "lucro líquido positivo" : "lucro líquido não positivo"));
            if (income > 0) signals.Add("Lucro líquido positivo."); else concerns.Add("Lucro líquido não positivo.");
        }
        if (snapshot.Roe is { } roe) evidence.Add(new("ROE", roe, "valor informado pela fonte"));
        if (snapshot.Equity is null && snapshot.NetIncome is not null) return new("Applicable", FinancialEvidenceCoverage.Partial, fresh ? FinancialEvidenceFreshness.Fresh : FinancialEvidenceFreshness.Stale, "WeakEvidence", signals, concerns, evidence, new[] { "Patrimônio líquido não disponível para contextualizar retorno." });
        var coverage = evidence.Count == 0 ? FinancialEvidenceCoverage.Unavailable : FinancialEvidenceCoverage.Partial;
        var category = concerns.Count > 0 ? "Weak" : signals.Count > 0 ? "Acceptable" : "Unavailable";
        return new("Applicable", coverage, fresh ? FinancialEvidenceFreshness.Fresh : FinancialEvidenceFreshness.Stale, category, signals, concerns, evidence, Array.Empty<string>());
    }

    private static CandidateValuationEvaluation Valuation(AssetType assetType, CandidateFinancialSnapshot? snapshot, DateTimeOffset now)
    {
        if (assetType != AssetType.Stock) return new("NotApplicable", FinancialEvidenceCoverage.Unavailable, FinancialEvidenceFreshness.Unavailable, "NotApplicable", Array.Empty<FinancialMetricEvidence>(), new[] { "Valuation de companhia não é aplicada automaticamente a este tipo de ativo." });
        if (snapshot is null) return new("Applicable", FinancialEvidenceCoverage.Unavailable, FinancialEvidenceFreshness.Unavailable, "InsufficientContext", Array.Empty<FinancialMetricEvidence>(), new[] { "Valuation não disponível." });
        var metrics = new List<FinancialMetricEvidence>();
        if (snapshot.PriceEarnings is { } pe && pe >= 0) metrics.Add(new("P/E", pe, "múltiplo informado pela fonte"));
        if (snapshot.PriceBook is { } pb && pb >= 0) metrics.Add(new("P/B", pb, "múltiplo informado pela fonte"));
        if (snapshot.EvEbitda is { } ev && ev >= 0) metrics.Add(new("EV/EBITDA", ev, "múltiplo informado pela fonte"));
        if (snapshot.DividendYield is { } yield && yield >= 0) metrics.Add(new("DividendYield", yield, "rendimento informado pela fonte"));
        var fresh = now - snapshot.ObservedAt <= TimeSpan.FromDays(35);
        return new("Applicable", metrics.Count == 0 ? FinancialEvidenceCoverage.Unavailable : FinancialEvidenceCoverage.Partial, fresh ? FinancialEvidenceFreshness.Fresh : FinancialEvidenceFreshness.Stale, "InsufficientContext", metrics, metrics.Count == 0 ? new[] { "Múltiplos de valuation não disponíveis." } : new[] { "Não há referência histórica ou comparável para classificar barato ou caro." });
    }
}
