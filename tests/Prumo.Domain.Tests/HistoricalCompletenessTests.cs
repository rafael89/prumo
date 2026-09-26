using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Historical;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Reconciliation;

namespace Prumo.Domain.Tests;

public sealed class HistoricalCompletenessTests
{
    [Fact]
    public async Task No_anchors_means_unavailable_without_inventing_reliable_boundary()
    {
        await using var db = CreateDb(); var portfolio = new Portfolio("Teste"); db.Add(portfolio); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var result = await Service(db).Get(portfolio.Id, null, null, TestContext.Current.CancellationToken);
        Assert.Equal("Unavailable", result!.OverallStatus); Assert.Null(result.ReliableFrom);
    }

    [Fact]
    public async Task Anchor_continuity_mismatch_degrades_position_without_mutating_anchors()
    {
        await using var db = CreateDb(); var portfolio = new Portfolio("Teste"); var asset = new Asset("PETR4", "Petrobras", AssetType.Stock, null); var first = new PortfolioHistoricalAnchor(portfolio.Id, new(2025, 1, 1), FinancialSource.Manual, FinancialDatePrecision.Exact); first.Items.Add(new PortfolioHistoricalAnchorItem(first.Id, asset.Id, 100, 20)); var second = new PortfolioHistoricalAnchor(portfolio.Id, new(2025, 2, 1), FinancialSource.Manual, FinancialDatePrecision.Exact); second.Items.Add(new PortfolioHistoricalAnchorItem(second.Id, asset.Id, 90, 20)); db.AddRange(portfolio, asset, first, second); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var result = await Service(db).Get(portfolio.Id, null, new(2025, 2, 1), TestContext.Current.CancellationToken);
        Assert.Contains(result!.Continuity, x => x.Dimension == "Position" && x.Status == "Mismatch" && x.Delta == 10); Assert.Equal("Partial", result.Dimensions.Single(x => x.Name == "Position").Status); Assert.Equal(100, (await db.PortfolioHistoricalAnchors.SingleAsync(x => x.Id == first.Id, cancellationToken: TestContext.Current.CancellationToken)).Items.First().Quantity);
    }

    [Fact]
    public async Task Market_prices_are_checked_at_each_relevant_interval_boundary()
    {
        await using var db = CreateDb(); var portfolio = new Portfolio("Teste"); var asset = new Asset("PETR4", "Petrobras", AssetType.Stock, null); var anchor = new PortfolioHistoricalAnchor(portfolio.Id, new(2025, 1, 1), FinancialSource.Manual, FinancialDatePrecision.Exact); anchor.Items.Add(new PortfolioHistoricalAnchorItem(anchor.Id, asset.Id, 100, 20)); db.AddRange(portfolio, asset, anchor, new AssetPrice(asset.Id, new(2025, 1, 2), 21, "BRL", "test", DateTimeOffset.UtcNow)); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var result = await Service(db).Get(portfolio.Id, new(2025, 1, 1), new(2025, 1, 2), TestContext.Current.CancellationToken);
        var prices = result!.Dimensions.Single(x => x.Name == "MarketPrices"); Assert.Equal("Partial", prices.Status); Assert.Contains(prices.Reasons, x => x.StartsWith("MissingMarketPrice:")); Assert.Equal("Partial", result.MetricReadiness.Single(x => x.Name == "HistoricalInvestedValuation").Status); Assert.Null(result.ReliableFrom);
    }

    [Fact]
    public void Existing_cash_flow_constructor_defaults_to_exact_precision()
    {
        var flow = new PortfolioCashFlow(Guid.NewGuid(), PortfolioCashFlowType.Contribution, 10, DateTimeOffset.UtcNow, FinancialSource.Manual);
        Assert.Equal(FinancialDatePrecision.Exact, flow.DatePrecision);
    }

    private static HistoricalCompletenessService Service(PrumoDbContext db) => new(db, new CoverageService(db), new HistoricalFinancialStateService(db, new CoverageService(db), new TransactionReconciliationService(db)), new HistoricalCashStateService(db, new CoverageService(db), new TransactionReconciliationService(db)));
    private static PrumoDbContext CreateDb() => new(new DbContextOptionsBuilder<PrumoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
