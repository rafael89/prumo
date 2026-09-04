using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Prumo.Api.Controllers;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.MarketData;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Domain.Tests;

public sealed class ExternalLifecyclePersistenceAcceptanceTests
{
    [Fact]
    public async Task Confirmation_reloads_snapshot_materializes_and_is_idempotent()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await using var db = CreateDb(databaseName);
        var portfolio = new Portfolio("teste");
        db.Portfolios.Add(portfolio);
        var identity = CandidateIdentity.FromExternal(new("Brapi", "EXT11", "B3", AssetType.Stock));
        var guidance = new PersistedContributionGuidance(portfolio.Id, 100, "s", "t", "i", "R16.5D", "test", "test", "Available", "r", "{}");
        guidance.ReplaceAllocations(new[] { new PersistedContributionGuidanceAllocation(guidance.Id, identity.Key.Value, "Brapi", "EXT11", "Empresa Externa", AssetClass.BrazilianStock, AssetType.Stock, "B3", "BRL", "Tecnologia", "verified", 100, "r", .9m, "[]") });
        db.ContributionGuidances.Add(guidance);
        await db.SaveChangesAsync();

        await using (var reloaded = CreateDb(databaseName))
        {
            var stored = await reloaded.ContributionGuidanceAllocations.SingleAsync();
            Assert.Equal(identity.Key.Value, stored.CandidateKey);
            Assert.Equal("EXT11", stored.Ticker);
            Assert.Equal("Brapi", stored.Provider);
            Assert.Equal(100, stored.Amount);
        }

        var first = await new PortfolioTransactionsController(db).Confirm(portfolio.Id,
            new ConfirmContributionRequest(guidance.Id, new[] { new ContributionPurchaseRequest(Guid.Empty, 1, 10, identity.Key.Value) }), CancellationToken.None);
        Assert.IsType<OkObjectResult>(first.Result);
        var asset = await db.Assets.SingleAsync(x => x.Ticker == "EXT11");
        Assert.Equal("Empresa Externa", asset.Name);
        Assert.Single(await db.PortfolioTransactions.Where(x => x.ContributionGuidanceId == guidance.Id).ToListAsync());
        Assert.Equal(100, (await db.PortfolioTransactions.SingleAsync()).TotalAmount);

        var second = await new PortfolioTransactionsController(db).Confirm(portfolio.Id,
            new ConfirmContributionRequest(guidance.Id, new[] { new ContributionPurchaseRequest(Guid.Empty, 1, 10, identity.Key.Value) }), CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(second.Result);
        Assert.Single(await db.Assets.Where(x => x.Ticker == "EXT11").ToListAsync());
        Assert.Single(await db.PortfolioTransactions.ToListAsync());
    }

    [Fact]
    public async Task Confirmation_reuses_existing_normalized_ticker_asset()
    {
        await using var db = CreateDb();
        var portfolio = new Portfolio("teste");
        var existing = new Asset("EXT11", "Já existente", AssetType.Stock, "Tecnologia");
        var identity = CandidateIdentity.FromExternal(new("Brapi", "ext11", "B3", AssetType.Stock));
        var guidance = new PersistedContributionGuidance(portfolio.Id, 50, "s", "t", "i", "R16.5D", "test", "test", "Available", "r", "{}");
        guidance.ReplaceAllocations(new[] { new PersistedContributionGuidanceAllocation(guidance.Id, identity.Key.Value, "Brapi", "EXT11", "Novo nome", AssetClass.BrazilianStock, AssetType.Stock, "B3", "BRL", null, "verified", 50, "r", .9m, "[]") });
        db.AddRange(portfolio, existing, guidance);
        await db.SaveChangesAsync();

        var result = await new PortfolioTransactionsController(db).Confirm(portfolio.Id,
            new ConfirmContributionRequest(guidance.Id, new[] { new ContributionPurchaseRequest(Guid.Empty, 1, 10, identity.Key.Value) }), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(await db.Assets.Where(x => x.Ticker == "EXT11").ToListAsync());
        Assert.Equal(existing.Id, (await db.PortfolioTransactionItems.SingleAsync()).AssetId);
    }

    [Fact]
    public async Task Forged_key_cannot_materialize_unattached_candidate()
    {
        await using var db = CreateDb();
        var portfolio = new Portfolio("teste");
        var identity = CandidateIdentity.FromExternal(new("Brapi", "EXT11", "B3", AssetType.Stock));
        var guidance = new PersistedContributionGuidance(portfolio.Id, 50, "s", "t", "i", "R16.5D", "test", "test", "Available", "r", "{}");
        guidance.ReplaceAllocations(new[] { new PersistedContributionGuidanceAllocation(guidance.Id, identity.Key.Value, "Brapi", "EXT11", "Externa", AssetClass.BrazilianStock, AssetType.Stock, "B3", "BRL", null, "verified", 50, "r", .9m, "[]") });
        db.AddRange(portfolio, guidance);
        await db.SaveChangesAsync();

        var result = await new PortfolioTransactionsController(db).Confirm(portfolio.Id,
            new ConfirmContributionRequest(guidance.Id, new[] { new ContributionPurchaseRequest(Guid.Empty, 1, 10, "ExternalDiscovery:Brapi:B3:FORGED") }), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await db.Assets.ToListAsync());
        Assert.Empty(await db.PortfolioTransactions.ToListAsync());
    }

    [Fact]
    public async Task Split_confirmation_materializes_external_and_keeps_known_asset()
    {
        await using var db = CreateDb();
        var portfolio = new Portfolio("teste");
        var known = new Asset("KNOWN3", "Conhecido", AssetType.Stock, null);
        var identity = CandidateIdentity.FromExternal(new("Brapi", "EXT11", "B3", AssetType.Stock));
        var guidance = new PersistedContributionGuidance(portfolio.Id, 100, "s", "t", "i", "R16.5D", "test", "test", "Available", "r", "{}");
        guidance.ReplaceAllocations(new PersistedContributionGuidanceAllocation[]
        {
            new(guidance.Id, known.Id, 40, "known", .9m, "[]"),
            new(guidance.Id, identity.Key.Value, "Brapi", "EXT11", "Externa", AssetClass.BrazilianStock, AssetType.Stock, "B3", "BRL", null, "verified", 60, "external", .9m, "[]")
        });
        db.AddRange(portfolio, known, guidance);
        await db.SaveChangesAsync();

        var result = await new PortfolioTransactionsController(db).Confirm(portfolio.Id,
            new ConfirmContributionRequest(guidance.Id, new[]
            {
                new ContributionPurchaseRequest(known.Id, 4, 10),
                new ContributionPurchaseRequest(Guid.Empty, 1, 10, identity.Key.Value)
            }), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(2, await db.PortfolioTransactionItems.CountAsync());
        Assert.Contains(known.Id, await db.PortfolioTransactionItems.Select(x => x.AssetId).ToListAsync());
        Assert.Single(await db.Assets.Where(x => x.Ticker == "EXT11").ToListAsync());
    }

    private static PrumoDbContext CreateDb(string? name = null)
    {
        var options = new DbContextOptionsBuilder<PrumoDbContext>().UseInMemoryDatabase(name ?? Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        return new PrumoDbContext(options);
    }
}
