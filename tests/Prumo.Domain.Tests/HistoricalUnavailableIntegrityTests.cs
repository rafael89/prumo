using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prumo.Api.Controllers;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Historical;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Performance;
using Prumo.Infrastructure.Reconciliation;

namespace Prumo.Domain.Tests;

public sealed class HistoricalUnavailableIntegrityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_anchor_stays_null_through_reconstruction_and_api(bool hasEndingAnchor)
    {
        await using var db = CreateDb();
        var portfolio = new Portfolio("unavailable"); var asset = new Asset("AAAA3", "A", AssetType.Stock, null);
        db.AddRange(portfolio, asset);
        if (hasEndingAnchor)
        {
            var anchor = new PortfolioHistoricalAnchor(portfolio.Id, new(2026, 9, 2), FinancialSource.Manual, FinancialDatePrecision.Exact);
            anchor.Items.Add(new(anchor.Id, asset.Id, 10, 10));
            db.AddRange(anchor, new AssetPrice(asset.Id, new(2026, 9, 2), 10, "BRL", "test", DateTimeOffset.UtcNow));
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var coverage = new CoverageService(db);
        var state = new HistoricalFinancialStateService(db, coverage, new TransactionReconciliationService(db));
        var opening = await state.Get(portfolio.Id, new(2026, 9, 1), null, default);
        Assert.Null(opening!.Anchor); Assert.Equal("Unavailable", opening.Coverage);
        var controller = new HistoricalPerformanceController(new HistoricalPerformanceService(db, state, coverage));
        var response = await controller.Get(portfolio.Id, new(2026, 9, 1), new(2026, 9, 2), default);
        var result = Assert.IsType<HistoricalPerformanceDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Null(result.StartingValuation.Value); Assert.Equal("Unavailable", result.StartingValuation.Coverage);
        Assert.Contains("NoAnchor", result.StartingValuation.Reasons);
        Assert.Null(result.ValueChange.Value); Assert.Equal("Unavailable", result.ValueChange.Coverage);
        if (hasEndingAnchor) Assert.Equal(100, result.EndingValuation.Value);
        else Assert.Null(result.EndingValuation.Value);
        var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("startingValuation").GetProperty("value").ValueKind);
        Assert.Equal("Unavailable", json.GetProperty("startingValuation").GetProperty("coverage").GetString());
    }

    [Fact]
    public async Task Fully_sold_observed_position_remains_real_zero_without_a_price()
    {
        await using var db = CreateDb(); var portfolio = new Portfolio("zero"); var asset = new Asset("AAAA3", "A", AssetType.Stock, null);
        var from = new DateOnly(2026, 9, 1); var to = from.AddDays(1);
        var anchor = new PortfolioHistoricalAnchor(portfolio.Id, from, FinancialSource.Manual, FinancialDatePrecision.Exact);
        anchor.Items.Add(new(anchor.Id, asset.Id, 10, 10));
        var sale = new PortfolioTransaction(portfolio.Id, PortfolioTransactionType.Sell, new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero), null, 100);
        sale.Items.Add(new(sale.Id, asset.Id, 10, 10));
        db.AddRange(portfolio, asset, anchor, sale);
        foreach (var dimension in new[] { FinancialHistoryCoverageDimension.Transactions, FinancialHistoryCoverageDimension.CorporateActions, FinancialHistoryCoverageDimension.Position, FinancialHistoryCoverageDimension.CostBasis })
            db.Add(new FinancialHistoryCoverage(portfolio.Id, dimension, from, to, FinancialSource.Manual));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var coverage = new CoverageService(db);
        var service = new HistoricalPerformanceService(db, new HistoricalFinancialStateService(db, coverage, new TransactionReconciliationService(db)), coverage);
        var result = await service.Get(portfolio.Id, from, to, default);
        Assert.Equal(0, result!.EndingValuation.Value); Assert.Equal("Complete", result.EndingValuation.Coverage);
        Assert.Equal(0, Assert.Single(result.EndingValuation.Assets).Quantity);
        Assert.Null(result.StartingValuation.Value); // Quantity exists, but its price is unknown.
    }

    private static PrumoDbContext CreateDb() => new(new DbContextOptionsBuilder<PrumoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
