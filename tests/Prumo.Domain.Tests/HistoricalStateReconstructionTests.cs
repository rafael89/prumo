using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Historical;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Reconciliation;

namespace Prumo.Domain.Tests;

public sealed class HistoricalStateReconstructionTests
{
    [Fact]
    public async Task Replays_buy_and_sell_with_weighted_average_cost()
    {
        await using var db = CreateDb(); var setup = await Setup(db, 100, 30);
        AddTransaction(db, setup, FinancialSource.Manual, new(2025, 1, 2), PortfolioTransactionType.Buy, 20, 40);
        AddTransaction(db, setup, FinancialSource.Manual, new(2025, 1, 3), PortfolioTransactionType.Sell, 10, 50); AddCoverage(db, setup, new(2025, 1, 1), new(2025, 1, 3)); await db.SaveChangesAsync();
        var result = await Service(db).Get(setup.Portfolio.Id, new(2025, 1, 3), null, default); var item = Assert.Single(result!.Assets);
        Assert.Equal(110, item.Quantity); Assert.InRange(item.AverageCost!.Value, 31.666m, 31.667m); Assert.Equal("Complete", item.QuantityCoverage);
    }

    [Fact]
    public async Task Unknown_opening_cost_stays_unknown_after_buy()
    {
        await using var db = CreateDb(); var setup = await Setup(db, 100, null); AddTransaction(db, setup, FinancialSource.Manual, new(2025, 1, 2), PortfolioTransactionType.Buy, 20, 40); AddCoverage(db, setup, new(2025, 1, 1), new(2025, 1, 2)); await db.SaveChangesAsync();
        var item = Assert.Single((await Service(db).Get(setup.Portfolio.Id, new(2025, 1, 2), null, default))!.Assets); Assert.Equal(120, item.Quantity); Assert.Null(item.TotalCostBasis); Assert.Equal("Unavailable", item.CostBasisCoverage);
    }

    [Fact]
    public async Task Oversell_is_unavailable_and_never_negative()
    {
        await using var db = CreateDb(); var setup = await Setup(db, 10, 30); AddTransaction(db, setup, FinancialSource.Manual, new(2025, 1, 2), PortfolioTransactionType.Sell, 20, 40); await db.SaveChangesAsync();
        var item = Assert.Single((await Service(db).Get(setup.Portfolio.Id, new(2025, 1, 2), null, default))!.Assets); Assert.Null(item.Quantity); Assert.Contains("Oversell", item.Warnings);
    }

    [Fact]
    public async Task Selects_latest_anchor_not_future_anchor()
    {
        await using var db = CreateDb(); var setup = await Setup(db, 100, 30, new(2025, 1, 1)); var later = new PortfolioHistoricalAnchor(setup.Portfolio.Id, new(2025, 2, 1), FinancialSource.Manual, FinancialDatePrecision.Exact); later.Items.Add(new PortfolioHistoricalAnchorItem(later.Id, setup.Asset.Id, 200, 30)); db.PortfolioHistoricalAnchors.Add(later); await db.SaveChangesAsync();
        var result = await Service(db).Get(setup.Portfolio.Id, new(2025, 1, 15), null, default); Assert.Equal(new(2025, 1, 1), result!.Anchor!.AnchorDate); Assert.Equal(100, Assert.Single(result.Assets).Quantity);
    }

    [Fact]
    public async Task Confirmed_duplicate_is_replayed_once()
    {
        await using var db = CreateDb(); var setup = await Setup(db, 100, 30); var a = AddTransaction(db, setup, FinancialSource.Manual, new(2025, 1, 2), PortfolioTransactionType.Buy, 10, 40); var b = AddTransaction(db, setup, FinancialSource.B3Import, new(2025, 1, 2), PortfolioTransactionType.Buy, 10, 40); AddCoverage(db, setup, new(2025, 1, 1), new(2025, 1, 2)); await db.SaveChangesAsync(); await new TransactionReconciliationService(db).Resolve(setup.Portfolio.Id, new([a.Id, b.Id], TransactionReconciliationStatus.ConfirmedSameEconomicTransaction), default);
        var item = Assert.Single((await Service(db).Get(setup.Portfolio.Id, new(2025, 1, 2), null, default))!.Assets); Assert.Equal(110, item.Quantity);
    }

    private static HistoricalFinancialStateService Service(PrumoDbContext db) => new(db, new CoverageService(db), new TransactionReconciliationService(db));
    private static void AddCoverage(PrumoDbContext db, SetupData setup, DateOnly from, DateOnly to) { db.FinancialHistoryCoverages.AddRange(new FinancialHistoryCoverage(setup.Portfolio.Id, FinancialHistoryCoverageDimension.Transactions, from, to, FinancialSource.Manual, setup.Asset.Id), new FinancialHistoryCoverage(setup.Portfolio.Id, FinancialHistoryCoverageDimension.CostBasis, from, to, FinancialSource.Manual, setup.Asset.Id), new FinancialHistoryCoverage(setup.Portfolio.Id, FinancialHistoryCoverageDimension.CorporateActions, from, to, FinancialSource.Manual, setup.Asset.Id)); }
    private static PortfolioTransaction AddTransaction(PrumoDbContext db, SetupData setup, FinancialSource source, DateOnly date, PortfolioTransactionType type, decimal quantity, decimal price) { var t = new PortfolioTransaction(setup.Portfolio.Id, type, date.ToDateTime(TimeOnly.MinValue), null, quantity * price, source: source); t.Items.Add(new PortfolioTransactionItem(t.Id, setup.Asset.Id, quantity, price)); db.PortfolioTransactions.Add(t); return t; }
    private static async Task<SetupData> Setup(PrumoDbContext db, decimal quantity, decimal? cost, DateOnly date = default) { if (date == default) date = new(2025, 1, 1); var portfolio = new Portfolio("Teste"); var asset = new Asset("PETR4", "Petrobras", AssetType.Stock, null); var anchor = new PortfolioHistoricalAnchor(portfolio.Id, date, FinancialSource.Manual, FinancialDatePrecision.Exact); anchor.Items.Add(new PortfolioHistoricalAnchorItem(anchor.Id, asset.Id, quantity, cost)); db.AddRange(portfolio, asset, anchor); await db.SaveChangesAsync(); return new(portfolio, asset); }
    private static PrumoDbContext CreateDb() => new(new DbContextOptionsBuilder<PrumoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed record SetupData(Portfolio Portfolio, Asset Asset);
}
