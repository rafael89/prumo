using Prumo.Application.Performance;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class PerformanceCalculatorTests
{
    [Fact]
    public void CalculatesWeightedAverageAndRealizedPnl()
    {
        var result = PerformanceCalculator.CalculateCostBasis([
            new(PortfolioTransactionType.Buy, 10, 100, At("2026-01-01"), FinancialDatePrecision.Exact),
            new(PortfolioTransactionType.Buy, 10, 120, At("2026-01-02"), FinancialDatePrecision.Exact),
            new(PortfolioTransactionType.Sell, 5, 150, At("2026-01-03"), FinancialDatePrecision.Exact)], 15, 110);

        Assert.Equal(1650, result.CostBasis);
        Assert.Equal(200, result.RealizedPnl);
        Assert.Equal(PerformanceCoverage.Complete, result.Coverage);
    }

    [Fact]
    public void DoesNotInventOpeningBasisForSellBeforeKnownHistory()
    {
        var result = PerformanceCalculator.CalculateCostBasis([
            new(PortfolioTransactionType.Sell, 5, 150, At("2026-01-01"), FinancialDatePrecision.Exact)], 5, 100);

        Assert.Null(result.RealizedPnl);
        Assert.Equal(PerformanceCoverage.Unavailable, result.Coverage);
    }

    [Fact]
    public void PriceReturnExcludesOtherFinancialEvents()
    {
        var result = PerformanceCalculator.PriceReturn(100, 110, DateOnly.Parse("2026-01-01"));

        Assert.Equal(0.1m, result.Value);
        Assert.Equal(PerformanceCoverage.Complete, result.Coverage);
    }

    [Fact]
    public void MissingPriceIsUnavailable()
    {
        var result = PerformanceCalculator.PriceReturn(null, 110, null);

        Assert.Null(result.Value);
        Assert.Equal(PerformanceCoverage.Unavailable, result.Coverage);
    }

    private static DateTimeOffset At(string date) => new(DateTime.Parse(date), TimeSpan.Zero);
}
