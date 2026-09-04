using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Historical;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Reconciliation;

namespace Prumo.Domain.Tests;

public sealed class HistoricalCashStateTests
{
    [Fact]
    public async Task Replays_cash_flows_and_canonical_buy_sell()
    {
        await using var db = CreateDb(); var s = await Setup(db, 1000); AddFlow(db, s, PortfolioCashFlowType.Contribution, 500, new(2025, 1, 2)); AddFlow(db, s, PortfolioCashFlowType.Income, 100, new(2025, 1, 3)); AddFlow(db, s, PortfolioCashFlowType.Withdrawal, 200, new(2025, 1, 4)); AddTransaction(db, s, PortfolioTransactionType.Buy, 10, 20, new(2025, 1, 5)); AddTransaction(db, s, PortfolioTransactionType.Sell, 5, 30, new(2025, 1, 6)); await db.SaveChangesAsync();
        var result = await Service(db).Get(s.Portfolio.Id, new(2025, 1, 6), default);
        Assert.Equal(1350, result!.Balance); Assert.Equal("Partial", result.Coverage);
    }

    [Fact]
    public async Task Complete_coverage_makes_known_cash_complete_and_negative_stays_negative()
    {
        await using var db = CreateDb(); var s = await Setup(db, 0); AddTransaction(db, s, PortfolioTransactionType.Buy, 10, 20, new(2025, 1, 2)); AddCoverage(db, s, new(2025, 1, 1), new(2025, 1, 2)); await db.SaveChangesAsync();
        var result = await Service(db).Get(s.Portfolio.Id, new(2025, 1, 2), default);
        Assert.Equal(-200, result!.Balance); Assert.Equal("Partial", result.Coverage); Assert.Contains("NegativeCashBalance", result.Warnings);
    }

    [Fact]
    public async Task Missing_anchor_does_not_assume_zero()
    {
        await using var db = CreateDb(); var portfolio = new Portfolio("Teste"); db.Add(portfolio); await db.SaveChangesAsync();
        var result = await Service(db).Get(portfolio.Id, new(2025, 1, 2), default);
        Assert.Null(result!.Balance); Assert.Equal("Unavailable", result.Coverage);
    }

    private static HistoricalCashStateService Service(PrumoDbContext db) => new(db, new CoverageService(db), new TransactionReconciliationService(db));
    private static void AddFlow(PrumoDbContext db, SetupData s, PortfolioCashFlowType type, decimal amount, DateOnly date) => db.PortfolioCashFlows.Add(new PortfolioCashFlow(s.Portfolio.Id, type, amount, date.ToDateTime(TimeOnly.MinValue), FinancialSource.Manual, type == PortfolioCashFlowType.Income ? s.Asset.Id : null));
    private static void AddTransaction(PrumoDbContext db, SetupData s, PortfolioTransactionType type, decimal quantity, decimal price, DateOnly date) { var t = new PortfolioTransaction(s.Portfolio.Id, type, date.ToDateTime(TimeOnly.MinValue), null, quantity * price, source: FinancialSource.Manual); t.Items.Add(new PortfolioTransactionItem(t.Id, s.Asset.Id, quantity, price)); db.PortfolioTransactions.Add(t); }
    private static void AddCoverage(PrumoDbContext db, SetupData s, DateOnly from, DateOnly to) => db.FinancialHistoryCoverages.AddRange(new FinancialHistoryCoverage(s.Portfolio.Id, FinancialHistoryCoverageDimension.Transactions, from, to, FinancialSource.Manual), new FinancialHistoryCoverage(s.Portfolio.Id, FinancialHistoryCoverageDimension.CashFlows, from, to, FinancialSource.Manual), new FinancialHistoryCoverage(s.Portfolio.Id, FinancialHistoryCoverageDimension.Income, from, to, FinancialSource.Manual));
    private static async Task<SetupData> Setup(PrumoDbContext db, decimal balance) { var portfolio = new Portfolio("Teste"); var asset = new Asset("PETR4", "Petrobras", AssetType.Stock, null); db.AddRange(portfolio, asset, new PortfolioCashAnchor(portfolio.Id, new(2025, 1, 1), balance, FinancialSource.Manual, FinancialDatePrecision.Exact)); await db.SaveChangesAsync(); return new(portfolio, asset); }
    private static PrumoDbContext CreateDb() => new(new DbContextOptionsBuilder<PrumoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed record SetupData(Portfolio Portfolio, Asset Asset);
}
