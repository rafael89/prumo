using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Historical;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Reconciliation;

namespace Prumo.Domain.Tests;

public sealed class CorporateActionTests
{
    [Fact]
    public void Rejects_invalid_factor() => Assert.Throws<ArgumentOutOfRangeException>(() => new AssetCorporateAction(Guid.NewGuid(), CorporateActionType.StockSplit, new(2025, 1, 1), FinancialDatePrecision.Exact, 0, 1, FinancialSource.Manual));

    [Fact]
    public async Task Corporate_action_crud_is_asset_scoped()
    {
        await using var db = CreateDb(); var s = await Setup(db, 100, 30); var service = new CorporateActionService(db);
        var created = await service.Create(s.Asset.Id, new(CorporateActionType.StockSplit, new(2025, 1, 10), FinancialDatePrecision.Exact, 2, 1, FinancialSource.Manual), TestContext.Current.CancellationToken);
        Assert.NotNull(await service.Get(s.Asset.Id, created.Id, TestContext.Current.CancellationToken)); Assert.Single(await service.List(s.Asset.Id, TestContext.Current.CancellationToken));
        await service.Update(s.Asset.Id, created.Id, new(CorporateActionType.StockSplit, new(2025, 1, 11), FinancialDatePrecision.Exact, 3, 1, FinancialSource.Manual), TestContext.Current.CancellationToken);
        Assert.Equal(new(2025, 1, 11), (await service.Get(s.Asset.Id, created.Id, TestContext.Current.CancellationToken))!.EffectiveDate);
        Assert.True(await service.Delete(s.Asset.Id, created.Id, TestContext.Current.CancellationToken)); Assert.Null(await service.Get(s.Asset.Id, created.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Split_preserves_basis_and_adjusts_average_cost()
    {
        await using var db = CreateDb(); var s = await Setup(db, 100, 30); db.AssetCorporateActions.Add(new AssetCorporateAction(s.Asset.Id, CorporateActionType.StockSplit, new(2025, 1, 10), FinancialDatePrecision.Exact, 2, 1, FinancialSource.Manual)); AddCoverage(db, s, new(2025, 1, 1), new(2025, 1, 10)); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var item = Assert.Single((await Service(db).Get(s.Portfolio.Id, new(2025, 1, 10), null, TestContext.Current.CancellationToken))!.Assets);
        Assert.Equal(200, item.Quantity); Assert.Equal(15, item.AverageCost); Assert.Equal(3000, item.TotalCostBasis); Assert.Single(item.AppliedCorporateActions);
    }

    [Fact]
    public async Task Reverse_split_keeps_fractional_quantity_without_rounding()
    {
        await using var db = CreateDb(); var s = await Setup(db, 15, null); db.AssetCorporateActions.Add(new AssetCorporateAction(s.Asset.Id, CorporateActionType.ReverseSplit, new(2025, 1, 10), FinancialDatePrecision.Exact, 1, 10, FinancialSource.Manual)); AddCoverage(db, s, new(2025, 1, 1), new(2025, 1, 10)); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var item = Assert.Single((await Service(db).Get(s.Portfolio.Id, new(2025, 1, 10), null, TestContext.Current.CancellationToken))!.Assets); Assert.Equal(1.5m, item.Quantity); Assert.Null(item.AverageCost);
    }

    [Fact]
    public async Task Same_day_action_is_applied_before_buy_and_buy_after_event_is_not_adjusted()
    {
        await using var db = CreateDb(); var s = await Setup(db, 0, 0); db.AssetCorporateActions.Add(new AssetCorporateAction(s.Asset.Id, CorporateActionType.StockSplit, new(2025, 1, 10), FinancialDatePrecision.Exact, 2, 1, FinancialSource.Manual)); var t = new PortfolioTransaction(s.Portfolio.Id, PortfolioTransactionType.Buy, new DateTime(2025, 1, 10), null, 100, source: FinancialSource.Manual); t.Items.Add(new PortfolioTransactionItem(t.Id, s.Asset.Id, 100, 1)); db.PortfolioTransactions.Add(t); AddCoverage(db, s, new(2025, 1, 1), new(2025, 1, 10)); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(100, Assert.Single((await Service(db).Get(s.Portfolio.Id, new(2025, 1, 10), null, TestContext.Current.CancellationToken))!.Assets).Quantity);
    }

    [Fact]
    public async Task Approximate_action_is_not_applied_and_degrades_coverage()
    {
        await using var db = CreateDb(); var s = await Setup(db, 100, 30); db.AssetCorporateActions.Add(new AssetCorporateAction(s.Asset.Id, CorporateActionType.StockSplit, new(2025, 1, 10), FinancialDatePrecision.Approximate, 2, 1, FinancialSource.Manual)); AddCoverage(db, s, new(2025, 1, 1), new(2025, 1, 10)); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var item = Assert.Single((await Service(db).Get(s.Portfolio.Id, new(2025, 1, 10), null, TestContext.Current.CancellationToken))!.Assets); Assert.Equal(100, item.Quantity); Assert.Contains("ApproximateCorporateActionDate", item.Warnings); Assert.Empty(item.AppliedCorporateActions);
    }

    private static HistoricalFinancialStateService Service(PrumoDbContext db) => new(db, new CoverageService(db), new TransactionReconciliationService(db));
    private static void AddCoverage(PrumoDbContext db, SetupData s, DateOnly from, DateOnly to) => db.FinancialHistoryCoverages.AddRange(new FinancialHistoryCoverage(s.Portfolio.Id, FinancialHistoryCoverageDimension.Position, from, from, FinancialSource.Manual, s.Asset.Id), new FinancialHistoryCoverage(s.Portfolio.Id, FinancialHistoryCoverageDimension.Transactions, from, to, FinancialSource.Manual, s.Asset.Id), new FinancialHistoryCoverage(s.Portfolio.Id, FinancialHistoryCoverageDimension.CostBasis, from, to, FinancialSource.Manual, s.Asset.Id), new FinancialHistoryCoverage(s.Portfolio.Id, FinancialHistoryCoverageDimension.CorporateActions, from, to, FinancialSource.Manual, s.Asset.Id));
    private static async Task<SetupData> Setup(PrumoDbContext db, decimal quantity, decimal? cost) { var portfolio = new Portfolio("Teste"); var asset = new Asset("PETR4", "Petrobras", AssetType.Stock, null); var anchor = new PortfolioHistoricalAnchor(portfolio.Id, new(2025, 1, 1), FinancialSource.Manual, FinancialDatePrecision.Exact); if (quantity > 0) anchor.Items.Add(new PortfolioHistoricalAnchorItem(anchor.Id, asset.Id, quantity, cost)); db.AddRange(portfolio, asset, anchor); await db.SaveChangesAsync(); return new(portfolio, asset); }
    private static PrumoDbContext CreateDb() => new(new DbContextOptionsBuilder<PrumoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed record SetupData(Portfolio Portfolio, Asset Asset);
}
