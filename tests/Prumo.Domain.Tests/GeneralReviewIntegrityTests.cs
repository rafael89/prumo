using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Prumo.Api.Controllers;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Historical;
using Prumo.Infrastructure.Reconciliation;

namespace Prumo.Domain.Tests;

public sealed class GeneralReviewIntegrityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [RelationalFact]
    public async Task Concurrent_target_replacements_preserve_one_complete_strategy()
    {
        const string admin = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=dev123";
        var database = $"prumo_review_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync(Ct);
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection))
            await create.ExecuteNonQueryAsync(Ct);
        try
        {
            var cs = new NpgsqlConnectionStringBuilder(admin) { Database = database }.ConnectionString;
            var options = new DbContextOptionsBuilder<PrumoDbContext>().UseNpgsql(cs).Options;
            var portfolio = new Portfolio("Concurrent targets");
            await using (var seed = new PrumoDbContext(options))
            {
                await seed.Database.MigrateAsync(Ct);
                seed.Add(portfolio);
                await seed.SaveChangesAsync(Ct);
            }
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<ActionResult> Replace(decimal percentage)
            {
                await using var db = new PrumoDbContext(options);
                await gate.Task;
                return await new TargetAllocationsController(db).Replace(portfolio.Id,
                    new([new(AssetClass.Fii, percentage), new(AssetClass.FixedIncome, 100 - percentage)]), Ct);
            }
            var first = Replace(30);
            var second = Replace(70);
            gate.SetResult();
            Assert.All(await Task.WhenAll(first, second), result => Assert.IsType<NoContentResult>(result));
            // Also exercise replacing existing rows under their unique index.
            await Replace(40);
            await using var verify = new PrumoDbContext(options);
            var targets = await verify.TargetAllocations.ToListAsync(Ct);
            Assert.Equal(2, targets.Count);
            Assert.Equal(100, targets.Sum(x => x.TargetPercentage));
            Assert.Equal(40, targets.Single(x => x.AssetClass == AssetClass.Fii).TargetPercentage);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static PrumoDbContext CreateDb() => new(new DbContextOptionsBuilder<PrumoDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task Invalid_historical_anchor_update_returns_validation_error_and_preserves_saved_items()
    {
        await using var db = CreateDb();
        var portfolio = new Portfolio("Review");
        var asset = new Asset("TEST3", "Test", AssetType.Stock, null);
        var date = new DateOnly(2026, 1, 1);
        var anchor = new PortfolioHistoricalAnchor(portfolio.Id, date, FinancialSource.Manual, FinancialDatePrecision.Exact);
        anchor.Items.Add(new PortfolioHistoricalAnchorItem(anchor.Id, asset.Id, 10, 100));
        db.AddRange(portfolio, asset, anchor);
        await db.SaveChangesAsync(Ct);
        var coverage = new CoverageService(db);
        var controller = new FinancialHistoryController(new HistoricalAnchorService(db), coverage,
            new HistoricalCashStateService(db, coverage, new TransactionReconciliationService(db)));
        var result = await controller.UpdateAnchor(portfolio.Id, anchor.Id,
            new(date, FinancialSource.Manual, FinancialDatePrecision.Exact, []), Ct);
        Assert.IsType<BadRequestObjectResult>(result.Result);
        db.ChangeTracker.Clear();
        Assert.Equal(10, (await db.PortfolioHistoricalAnchorItems.SingleAsync(Ct)).Quantity);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Additional_purchase_preserves_existing_cost_and_records_execution_price(bool explicitPrice)
    {
        await using var db = CreateDb();
        var portfolio = new Portfolio("Review");
        var asset = new Asset("TEST3", "Test", AssetType.Stock, null);
        var position = new PortfolioPosition(portfolio.Id, asset.Id, 10, 100, 120);
        db.AddRange(portfolio, asset, position);
        await db.SaveChangesAsync(Ct);

        var result = await new PortfoliosController(db).UpdatePosition(portfolio.Id, position.Id,
            new(15, explicitPrice ? 100 : 200, 130, true, PurchaseUnitPrice: explicitPrice ? 200 : null), Ct);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(15, position.Quantity);
        Assert.Equal(2000m / 15, position.AveragePrice);
        Assert.Equal(1950, position.CurrentValue);
        var transaction = await db.PortfolioTransactions.Include(x => x.Items).SingleAsync(Ct);
        Assert.Equal(1000, transaction.TotalAmount);
        Assert.Equal(5, transaction.Items.Single().Quantity);
        Assert.Equal(200, transaction.Items.Single().UnitPrice);
    }

    [Fact]
    public async Task Purchase_without_quantity_increase_is_rejected_without_changes()
    {
        await using var db = CreateDb();
        var portfolio = new Portfolio("Review");
        var asset = new Asset("TEST3", "Test", AssetType.Stock, null);
        var position = new PortfolioPosition(portfolio.Id, asset.Id, 10, 100, 120);
        db.AddRange(portfolio, asset, position);
        await db.SaveChangesAsync(Ct);
        var result = await new PortfoliosController(db).UpdatePosition(portfolio.Id, position.Id,
            new(5, 200, 130, true), Ct);
        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(10, position.Quantity);
        Assert.Empty(await db.PortfolioTransactions.ToListAsync(Ct));
    }

    [Fact]
    public async Task Ledger_preserves_cash_flow_date_precision()
    {
        await using var db = CreateDb();
        var portfolio = new Portfolio("Review");
        db.AddRange(portfolio, new PortfolioCashFlow(portfolio.Id, PortfolioCashFlowType.Contribution,
            100, DateTimeOffset.UtcNow, FinancialSource.Manual, datePrecision: FinancialDatePrecision.Approximate));
        await db.SaveChangesAsync(Ct);
        var result = await new FinancialLedgerController(db).Get(portfolio.Id, Ct);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<LedgerItemDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Approximate", Assert.Single(rows).DatePrecision);
    }

    [Fact]
    public async Task Cash_flow_rejects_unknown_optional_asset()
    {
        await using var db = CreateDb();
        var portfolio = new Portfolio("Review");
        db.Add(portfolio);
        await db.SaveChangesAsync(Ct);
        var result = await new FinancialLedgerController(db).CashFlow(portfolio.Id,
            new(PortfolioCashFlowType.Contribution, 100, DateTimeOffset.UtcNow, Guid.NewGuid()), Ct);
        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await db.PortfolioCashFlows.ToListAsync(Ct));
    }

    [Fact]
    public void Cash_flow_rejects_zero_after_rounding_and_invalid_precision()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PortfolioCashFlow(Guid.NewGuid(),
            PortfolioCashFlowType.Contribution, .001m, DateTimeOffset.UtcNow, FinancialSource.Manual));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PortfolioCashFlow(Guid.NewGuid(),
            PortfolioCashFlowType.Contribution, 10, DateTimeOffset.UtcNow, FinancialSource.Manual,
            datePrecision: (FinancialDatePrecision)999));
    }
}
