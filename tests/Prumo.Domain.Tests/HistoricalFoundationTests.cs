using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Historical;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Domain.Tests;

public sealed class HistoricalFoundationTests
{
    [Fact]
    public void Anchor_requires_positive_quantity_and_allows_unknown_cost()
    {
        var anchor = new PortfolioHistoricalAnchor(Guid.NewGuid(), new DateOnly(2025, 1, 1), FinancialSource.Manual, FinancialDatePrecision.Exact);
        var item = new PortfolioHistoricalAnchorItem(anchor.Id, Guid.NewGuid(), 100, null);
        anchor.Items.Add(item);
        Assert.Null(item.AverageCost);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PortfolioHistoricalAnchorItem(anchor.Id, Guid.NewGuid(), 0, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PortfolioHistoricalAnchorItem(anchor.Id, Guid.NewGuid(), -1, 10));
    }

    [Fact]
    public void Coverage_rejects_invalid_interval()
        => Assert.Throws<ArgumentException>(() => new FinancialHistoryCoverage(Guid.NewGuid(), FinancialHistoryCoverageDimension.Transactions, new DateOnly(2025, 2, 1), new DateOnly(2025, 1, 1), FinancialSource.Manual));

    [Fact]
    public async Task Coverage_unions_overlaps_and_detects_gaps()
    {
        await using var db = CreateDb(); var portfolio = new Portfolio("Teste"); db.Portfolios.Add(portfolio); await db.SaveChangesAsync();
        var service = new CoverageService(db);
        await service.Create(portfolio.Id, new(FinancialHistoryCoverageDimension.Transactions, new(2025, 1, 1), new(2025, 6, 30), FinancialSource.Manual), default);
        await service.Create(portfolio.Id, new(FinancialHistoryCoverageDimension.Transactions, new(2025, 7, 1), new(2025, 12, 31), FinancialSource.Manual), default);
        var complete = await service.Resolve(portfolio.Id, FinancialHistoryCoverageDimension.Transactions, new(2025, 1, 1), new(2025, 12, 31), null, default);
        Assert.Equal("Complete", complete.Status);
        await service.Delete(portfolio.Id, complete.Evidence[0].Id, default);
        var partial = await service.Resolve(portfolio.Id, FinancialHistoryCoverageDimension.Transactions, new(2025, 1, 1), new(2025, 12, 31), null, default);
        Assert.Equal("Partial", partial.Status);
    }

    [Fact]
    public async Task Coverage_does_not_cross_dimensions_or_assets()
    {
        await using var db = CreateDb(); var portfolio = new Portfolio("Teste"); var assetA = new Asset("AAA3", "A", AssetType.Stock, null); var assetB = new Asset("BBB3", "B", AssetType.Stock, null); db.AddRange(portfolio, assetA, assetB); await db.SaveChangesAsync();
        var service = new CoverageService(db);
        await service.Create(portfolio.Id, new(FinancialHistoryCoverageDimension.Transactions, new(2025, 1, 1), new(2025, 12, 31), FinancialSource.Manual, assetA.Id), default);
        Assert.Equal("Complete", (await service.Resolve(portfolio.Id, FinancialHistoryCoverageDimension.Transactions, new(2025, 1, 1), new(2025, 12, 31), assetA.Id, default)).Status);
        Assert.Equal("Unavailable", (await service.Resolve(portfolio.Id, FinancialHistoryCoverageDimension.CostBasis, new(2025, 1, 1), new(2025, 12, 31), assetA.Id, default)).Status);
        Assert.Equal("Unavailable", (await service.Resolve(portfolio.Id, FinancialHistoryCoverageDimension.Transactions, new(2025, 1, 1), new(2025, 12, 31), assetB.Id, default)).Status);
    }

    private static PrumoDbContext CreateDb() => new(new DbContextOptionsBuilder<PrumoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
