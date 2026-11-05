using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Reconciliation;

namespace Prumo.Domain.Tests;

public sealed class TransactionReconciliationTests
{
    [Fact]
    public async Task Exact_different_sources_are_candidate_then_count_once_when_confirmed()
    {
        await using var db = CreateDb(); var portfolio = new Portfolio("Teste"); var asset = new Asset("PETR4", "Petrobras", AssetType.Stock, null); db.AddRange(portfolio, asset);
        var manual = AddTransaction(db, portfolio, asset, FinancialSource.Manual, new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
        var b3 = AddTransaction(db, portfolio, asset, FinancialSource.B3Import, new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new TransactionReconciliationService(db);
        Assert.Single(await service.Candidates(portfolio.Id, TestContext.Current.CancellationToken));
        await service.Resolve(portfolio.Id, new([manual.Id, b3.Id], TransactionReconciliationStatus.ConfirmedSameEconomicTransaction), TestContext.Current.CancellationToken);
        var canonical = await service.Canonical(portfolio.Id, TestContext.Current.CancellationToken);
        Assert.Single(canonical); Assert.Equal(2, canonical[0].SourceRecordIds.Count); Assert.Equal(2, await db.PortfolioTransactions.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Candidate_does_not_change_count_and_same_source_is_ignored()
    {
        await using var db = CreateDb(); var portfolio = new Portfolio("Teste"); var asset = new Asset("VALE3", "Vale", AssetType.Stock, null); db.AddRange(portfolio, asset);
        AddTransaction(db, portfolio, asset, FinancialSource.B3Import, DateTimeOffset.UtcNow); AddTransaction(db, portfolio, asset, FinancialSource.B3Import, DateTimeOffset.UtcNow); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new TransactionReconciliationService(db); Assert.Empty(await service.Candidates(portfolio.Id, TestContext.Current.CancellationToken)); Assert.Equal(2, (await service.Canonical(portfolio.Id, TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task Buy_and_sell_are_never_candidates()
    {
        await using var db = CreateDb(); var portfolio = new Portfolio("Teste"); var asset = new Asset("ITSA4", "Itaú", AssetType.Stock, null); db.AddRange(portfolio, asset);
        AddTransaction(db, portfolio, asset, FinancialSource.Manual, DateTimeOffset.UtcNow, PortfolioTransactionType.Buy); AddTransaction(db, portfolio, asset, FinancialSource.B3Import, DateTimeOffset.UtcNow, PortfolioTransactionType.Sell); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Empty(await new TransactionReconciliationService(db).Candidates(portfolio.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Approximate_date_can_match_without_replacing_exact_date()
    {
        await using var db = CreateDb(); var portfolio = new Portfolio("Teste"); var asset = new Asset("HGLG11", "HGLG", AssetType.Fii, null); db.AddRange(portfolio, asset);
        AddTransaction(db, portfolio, asset, FinancialSource.Manual, new(2026, 8, 14, 0, 0, 0, TimeSpan.Zero)); AddTransaction(db, portfolio, asset, FinancialSource.B3Import, new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), datePrecision: FinancialDatePrecision.PeriodStart); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var candidate = Assert.Single(await new TransactionReconciliationService(db).Candidates(portfolio.Id, TestContext.Current.CancellationToken)); Assert.Equal("Possible", candidate.MatchStrength);
    }

    [Fact]
    public async Task Auto_reconciliation_resolves_only_an_exact_one_to_one_match()
    {
        await using var db = CreateDb(); var portfolio = new Portfolio("Teste"); var asset = new Asset("PETR4", "Petrobras", AssetType.Stock, null); db.AddRange(portfolio, asset);
        AddTransaction(db, portfolio, asset, FinancialSource.Manual, new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)); AddTransaction(db, portfolio, asset, FinancialSource.B3Import, new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new TransactionReconciliationService(db);
        Assert.Equal(1, await service.AutoResolveDeterministic(portfolio.Id, TestContext.Current.CancellationToken));
        Assert.Empty(await service.Candidates(portfolio.Id, TestContext.Current.CancellationToken));
        Assert.Single(await service.Canonical(portfolio.Id, TestContext.Current.CancellationToken));
        Assert.Equal(0, await service.AutoResolveDeterministic(portfolio.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Ambiguous_reconciliation_remains_an_actionable_candidate()
    {
        await using var db = CreateDb(); var portfolio = new Portfolio("Teste"); var asset = new Asset("PETR4", "Petrobras", AssetType.Stock, null); db.AddRange(portfolio, asset);
        AddTransaction(db, portfolio, asset, FinancialSource.Manual, new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)); AddTransaction(db, portfolio, asset, FinancialSource.B3Import, new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)); AddTransaction(db, portfolio, asset, FinancialSource.B3Import, new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new TransactionReconciliationService(db);
        Assert.Equal(0, await service.AutoResolveDeterministic(portfolio.Id, TestContext.Current.CancellationToken));
        Assert.Equal(2, (await service.Candidates(portfolio.Id, TestContext.Current.CancellationToken)).Count);
        Assert.Equal(3, (await service.Canonical(portfolio.Id, TestContext.Current.CancellationToken)).Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Auto_reconciliation_does_not_depend_on_insertion_order(int importedIndex)
    {
        await using var db = CreateDb();
        var portfolio = new Portfolio("Ambiguous");
        var asset = new Asset("PETR4", "Petrobras", AssetType.Stock, null);
        db.AddRange(portfolio, asset);
        for (var i = 0; i < 3; i++)
            AddTransaction(db, portfolio, asset, i == importedIndex ? FinancialSource.B3Import : FinancialSource.Manual,
                new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new TransactionReconciliationService(db);

        Assert.Equal(0, await service.AutoResolveDeterministic(portfolio.Id, TestContext.Current.CancellationToken));
        Assert.Empty(await service.List(portfolio.Id, TestContext.Current.CancellationToken));
        Assert.Equal(3, (await service.Canonical(portfolio.Id, TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task Period_start_matching_a_trade_date_is_not_an_exact_match()
    {
        await using var db = CreateDb();
        var portfolio = new Portfolio("Approximate date");
        var asset = new Asset("PETR4", "Petrobras", AssetType.Stock, null);
        db.AddRange(portfolio, asset);
        var date = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        AddTransaction(db, portfolio, asset, FinancialSource.Manual, date);
        AddTransaction(db, portfolio, asset, FinancialSource.B3Import, date, datePrecision: FinancialDatePrecision.PeriodStart);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new TransactionReconciliationService(db);

        Assert.Equal(0, await service.AutoResolveDeterministic(portfolio.Id, TestContext.Current.CancellationToken));
        Assert.Single(await service.Candidates(portfolio.Id, TestContext.Current.CancellationToken));
    }

    private static PortfolioTransaction AddTransaction(PrumoDbContext db, Portfolio portfolio, Asset asset, FinancialSource source, DateTimeOffset date, PortfolioTransactionType type = PortfolioTransactionType.Buy, FinancialDatePrecision datePrecision = FinancialDatePrecision.Exact)
    {
        var transaction = new PortfolioTransaction(portfolio.Id, type, date, null, 3200, source: source, datePrecision: datePrecision); transaction.Items.Add(new PortfolioTransactionItem(transaction.Id, asset.Id, 100, 32)); db.PortfolioTransactions.Add(transaction); return transaction;
    }

    private static PrumoDbContext CreateDb() => new(new DbContextOptionsBuilder<PrumoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
