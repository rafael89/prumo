using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Prumo.Api.Controllers;
using Prumo.Api.Services;
using Prumo.Application.Intelligence;
using Prumo.Application.Portfolio;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Domain.Tests;

public sealed class ContributionExecutionIntegrityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Only_executed_assets_change_positions_and_later_execution_remains_possible(bool split)
    {
        await using var db = CreateDb();
        var portfolio = new Portfolio("executions");
        var a = new Asset("AAAA3", "A", AssetType.Stock, null);
        var b = new Asset("BBBB3", "B", AssetType.Stock, null);
        var guidance = new PersistedContributionGuidance(portfolio.Id, 1000, "s", "t", "i", "test", "test", "test", "Available", "test", "{}");
        guidance.Allocations.Add(new(guidance.Id, a.Id, 600, "A", 1, "[]"));
        if (split) guidance.Allocations.Add(new(guidance.Id, b.Id, 400, "B", 1, "[]"));
        var job = new ContributionGuidanceJob(portfolio.Id, guidance.Id, "{}"); job.Start(); job.Complete();
        db.AddRange(portfolio, a, b, guidance, job, new PortfolioPosition(portfolio.Id, a.Id, 10, 50, 100));
        await db.SaveChangesAsync();
        var firstDate = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var laterDate = firstDate.AddDays(1);
        var controller = new PortfolioTransactionsController(db);
        var first = await controller.Confirm(portfolio.Id, new(guidance.Id, [new(a.Id, 2, 100)], firstDate), default);
        Assert.IsType<OkObjectResult>(first.Result);
        db.ChangeTracker.Clear();
        var aPosition = await db.PortfolioPositions.SingleAsync();
        Assert.Equal(a.Id, aPosition.AssetId); Assert.Equal(12, aPosition.Quantity);
        Assert.Equal(700m / 12, aPosition.AveragePrice);
        var firstTransaction = await db.PortfolioTransactions.Include(x => x.Items).SingleAsync();
        Assert.Equal(200, firstTransaction.TotalAmount); Assert.Equal(firstDate, firstTransaction.OccurredAt);
        Assert.Equal(a.Id, Assert.Single(firstTransaction.Items).AssetId);
        var duplicate = await controller.Confirm(portfolio.Id, new(guidance.Id, [new(a.Id, 2, 100)], firstDate), default);
        Assert.IsType<ConflictObjectResult>(duplicate.Result);

        if (split)
        {
            var response = await GuidanceController(db).Get(portfolio.Id, guidance.Id, default);
            var json = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(response.Result).Value);
            Assert.True(json.GetProperty("allocations")[0].GetProperty("executed").GetBoolean());
            Assert.False(json.GetProperty("allocations")[1].GetProperty("executed").GetBoolean());
            var later = await controller.Confirm(portfolio.Id, new(guidance.Id, [new(b.Id, 3, 90)], laterDate), default);
            Assert.IsType<OkObjectResult>(later.Result);
            db.ChangeTracker.Clear();
            var transactions = await db.PortfolioTransactions.Include(x => x.Items).OrderBy(x => x.OccurredAt).ToListAsync();
            Assert.Equal(2, transactions.Count);
            Assert.Equal(270, transactions[1].TotalAmount); Assert.Equal(laterDate, transactions[1].OccurredAt);
            var item = Assert.Single(transactions[1].Items);
            Assert.Equal(b.Id, item.AssetId); Assert.Equal(3, item.Quantity); Assert.Equal(90, item.UnitPrice);
            Assert.Equal(12, (await db.PortfolioPositions.SingleAsync(x => x.AssetId == a.Id)).Quantity);
            Assert.Equal(3, (await db.PortfolioPositions.SingleAsync(x => x.AssetId == b.Id)).Quantity);
        }
    }

    [Fact]
    public async Task Partial_confirmations_respect_cumulative_guidance_budget()
    {
        await using var db = CreateDb();
        var portfolio = new Portfolio("budget");
        var a = new Asset("AAAA3", "A", AssetType.Stock, null); var b = new Asset("BBBB3", "B", AssetType.Stock, null);
        var guidance = new PersistedContributionGuidance(portfolio.Id, 100, "s", "t", "i", "test", "test", "test", "Available", "r", "{}");
        db.AddRange(portfolio, a, b, guidance); await db.SaveChangesAsync();
        var controller = new PortfolioTransactionsController(db);
        Assert.IsType<OkObjectResult>((await controller.Confirm(portfolio.Id, new(guidance.Id, [new(a.Id, 1, 60)]), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.Confirm(portfolio.Id, new(guidance.Id, [new(b.Id, 1, 50)]), default)).Result);
        Assert.Single(await db.PortfolioTransactions.ToListAsync());
        Assert.IsType<OkObjectResult>((await controller.Confirm(portfolio.Id, new(guidance.Id, [new(b.Id, 1, 40)]), default)).Result);
        Assert.Equal(100, await db.PortfolioTransactions.SumAsync(x => x.TotalAmount));
    }

    private static PrumoDbContext CreateDb() => new(new DbContextOptionsBuilder<PrumoDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static ContributionGuidanceController GuidanceController(PrumoDbContext db) => new(db,
        new PortfolioAssessmentCurrentService(db, new PortfolioImpactAssessmentService(), new PortfolioAssessmentFreshnessEvaluator(), NullLogger<PortfolioAssessmentCurrentService>.Instance));
}
