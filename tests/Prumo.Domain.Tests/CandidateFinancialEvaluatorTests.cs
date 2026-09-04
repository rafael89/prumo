using Prumo.Application.Contributions;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class CandidateFinancialEvaluatorTests
{
    [Fact]
    public void Uses_fresh_positive_profitability_as_quality_evidence()
    {
        var result = CandidateFinancialEvaluator.Evaluate(Guid.NewGuid(), AssetType.Stock,
            new CandidateFinancialSnapshot(new(2026, 6, 30), FinancialPeriodType.Quarterly, new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), FinancialDataSource.Provider, NetIncome: 100, Equity: 500));

        Assert.Equal("Acceptable", result.Quality.Category);
        Assert.Contains(result.Quality.Signals, x => x.Contains("positivo"));
        Assert.Equal(FinancialEvidenceFreshness.Fresh, result.Quality.Freshness);
    }

    [Fact]
    public void Does_not_apply_company_metrics_to_etf()
    {
        var result = CandidateFinancialEvaluator.Evaluate(Guid.NewGuid(), AssetType.Etf, null);

        Assert.Equal("NotApplicable", result.Quality.Applicability);
        Assert.Equal("NotApplicable", result.Valuation.Applicability);
    }

    [Fact]
    public void Does_not_call_low_pe_cheap_without_context()
    {
        var result = CandidateFinancialEvaluator.Evaluate(Guid.NewGuid(), AssetType.Stock,
            new CandidateFinancialSnapshot(new(2026, 8, 1), FinancialPeriodType.Ttm, DateTimeOffset.UtcNow, FinancialDataSource.Provider, PriceEarnings: 4));

        Assert.Equal("InsufficientContext", result.Valuation.Assessment);
        Assert.Contains(result.Valuation.MissingData, x => x.Contains("referência"));
    }
}
