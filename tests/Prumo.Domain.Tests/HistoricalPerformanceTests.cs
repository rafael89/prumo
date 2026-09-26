using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Historical;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Performance;
using Prumo.Infrastructure.Reconciliation;

namespace Prumo.Domain.Tests;

public sealed class HistoricalPerformanceTests
{
    [Fact]
    public async Task Uses_latest_price_on_or_before_date_and_calculates_value_change()
    {
        await using var db = CreateDb(); var portfolio = new Portfolio("Teste"); var asset = new Asset("PETR4", "Petrobras", AssetType.Stock, null); var anchor = new PortfolioHistoricalAnchor(portfolio.Id, new(2025, 1, 1), FinancialSource.Manual, FinancialDatePrecision.Exact); anchor.Items.Add(new PortfolioHistoricalAnchorItem(anchor.Id, asset.Id, 100, 30)); db.AddRange(portfolio, asset, anchor, new AssetPrice(asset.Id, new(2025, 1, 1), 30, "BRL", "test", DateTimeOffset.UtcNow), new AssetPrice(asset.Id, new(2025, 1, 3), 35, "BRL", "test", DateTimeOffset.UtcNow)); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var result = await Service(db).Get(portfolio.Id, new(2025, 1, 1), new(2025, 1, 3), TestContext.Current.CancellationToken);
        Assert.Equal(3000, result!.StartingValuation.Value); Assert.Equal(3500, result.EndingValuation.Value); Assert.Equal(500, result.ValueChange.Value); Assert.DoesNotContain(result.EndingValuation.Assets, x => x.PriceDate > new DateOnly(2025, 1, 3));
    }

    [Fact]
    public async Task Missing_price_does_not_become_zero()
    {
        await using var db = CreateDb(); var portfolio = new Portfolio("Teste"); var asset = new Asset("VALE3", "Vale", AssetType.Stock, null); var anchor = new PortfolioHistoricalAnchor(portfolio.Id, new(2025, 1, 1), FinancialSource.Manual, FinancialDatePrecision.Exact); anchor.Items.Add(new PortfolioHistoricalAnchorItem(anchor.Id, asset.Id, 10, 20)); db.AddRange(portfolio, asset, anchor); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var result = await Service(db).Get(portfolio.Id, new(2025, 1, 1), new(2025, 1, 2), TestContext.Current.CancellationToken);
        Assert.Null(result!.EndingValuation.Value); Assert.Equal("Unavailable", result.EndingValuation.Coverage);
    }

    [Fact]
    public async Task Explicit_flows_are_reported_without_treating_buy_as_contribution()
    {
        await using var db = CreateDb(); var portfolio = new Portfolio("Teste"); var asset = new Asset("ITSA4", "Itaú", AssetType.Stock, null); var anchor = new PortfolioHistoricalAnchor(portfolio.Id, new(2025, 1, 1), FinancialSource.Manual, FinancialDatePrecision.Exact); anchor.Items.Add(new PortfolioHistoricalAnchorItem(anchor.Id, asset.Id, 10, 20)); db.AddRange(portfolio, asset, anchor, new PortfolioCashFlow(portfolio.Id, PortfolioCashFlowType.Contribution, 500, new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero), FinancialSource.Manual), new PortfolioCashFlow(portfolio.Id, PortfolioCashFlowType.Withdrawal, 100, new DateTimeOffset(2025, 1, 3, 0, 0, 0, TimeSpan.Zero), FinancialSource.Manual)); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var result = await Service(db).Get(portfolio.Id, new(2025, 1, 1), new(2025, 1, 3), TestContext.Current.CancellationToken);
        Assert.Equal(500, result!.Contributions.Value); Assert.Equal(100, result.Withdrawals.Value); Assert.Equal(400, result.NetExternalFlows.Value);
    }

    [Fact]
    public async Task Includes_total_value_when_cash_is_known()
    {
        await using var db = CreateDb(); var portfolio = new Portfolio("Teste"); var asset = new Asset("PETR4", "Petrobras", AssetType.Stock, null); var anchor = new PortfolioHistoricalAnchor(portfolio.Id, new(2025, 1, 1), FinancialSource.Manual, FinancialDatePrecision.Exact); anchor.Items.Add(new PortfolioHistoricalAnchorItem(anchor.Id, asset.Id, 100, 30)); db.AddRange(portfolio, asset, anchor, new PortfolioCashAnchor(portfolio.Id, new(2025, 1, 1), 2000, FinancialSource.Manual, FinancialDatePrecision.Exact), new AssetPrice(asset.Id, new(2025, 1, 1), 100, "BRL", "test", DateTimeOffset.UtcNow)); AddCoverage(db, portfolio.Id, new(2025, 1, 1), new(2025, 1, 1)); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var result = await Service(db).Get(portfolio.Id, new(2025, 1, 1), new(2025, 1, 1), TestContext.Current.CancellationToken);
        Assert.Equal(10000, result!.StartingValuation.Value); Assert.Equal(2000, result.StartingValuation.CashBalance); Assert.Equal(12000, result.StartingValuation.TotalValue);
    }

    private static void AddCoverage(PrumoDbContext db, Guid portfolioId, DateOnly from, DateOnly to) => db.FinancialHistoryCoverages.AddRange(new FinancialHistoryCoverage(portfolioId, FinancialHistoryCoverageDimension.Transactions, from, to, FinancialSource.Manual), new FinancialHistoryCoverage(portfolioId, FinancialHistoryCoverageDimension.CashFlows, from, to, FinancialSource.Manual), new FinancialHistoryCoverage(portfolioId, FinancialHistoryCoverageDimension.Income, from, to, FinancialSource.Manual), new FinancialHistoryCoverage(portfolioId, FinancialHistoryCoverageDimension.Position, from, from, FinancialSource.Manual, null), new FinancialHistoryCoverage(portfolioId, FinancialHistoryCoverageDimension.CostBasis, from, to, FinancialSource.Manual));
    private static HistoricalPerformanceService Service(PrumoDbContext db) => new(db, new HistoricalFinancialStateService(db, new CoverageService(db), new TransactionReconciliationService(db), new HistoricalCashStateService(db, new CoverageService(db), new TransactionReconciliationService(db))), new CoverageService(db));
    private static PrumoDbContext CreateDb() => new(new DbContextOptionsBuilder<PrumoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
