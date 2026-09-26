using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Prumo.Api.Services;
using Prumo.Api.Controllers;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Domain.Tests;

public sealed class DecisionMemoryAcceptanceTests
{
    [Fact]
    public async Task Confirmed_guidance_history_keeps_persisted_decision_and_execution()
    {
        await using var db = Db();
        var portfolio = new Portfolio("Teste");
        var asset = new Asset("ABCD3", "Ativo", AssetType.Stock, "Tecnologia");
        var guidance = Guidance(portfolio, "ReinforceExisting", asset, 650m);
        var transaction = new PortfolioTransaction(portfolio.Id, PortfolioTransactionType.Buy, DateTimeOffset.UtcNow, guidance.Id, 650m, source: FinancialSource.ContributionGuidanceExecution);
        transaction.Items.Add(new PortfolioTransactionItem(transaction.Id, asset.Id, 10, 65));
        db.AddRange(portfolio, asset, guidance, transaction);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var item = (await new PortfolioHistoryService(db).GetTimelineAsync(portfolio.Id, 100, null, TestContext.Current.CancellationToken))!.Items.Single(x => x.Type == "DecisionMemory");
        Assert.Equal(guidance.Id, item.RelatedEntityId);
        Assert.Equal("ReinforceExisting", item.DecisionType);
        Assert.Equal(650m, item.ContributionAmount);
        Assert.Equal("Confirmed", item.ConfirmationStatus);
        Assert.Equal(transaction.Id, item.TransactionId);
        Assert.Equal("ABCD3", item.AllocationTickers!.Single());
        Assert.Contains("Concentration", item.Evidence!);
    }

    [Fact]
    public async Task Unconfirmed_external_guidance_history_needs_no_asset_id()
    {
        await using var db = Db();
        var portfolio = new Portfolio("Teste");
        var guidance = new PersistedContributionGuidance(portfolio.Id, 350m, "p", "t", "i", "R16.7", "deterministic", "rules", "Deterministic", "reason", "{\"decisionType\":\"AddNewAsset\",\"decisionEvidence\":{\"decisiveFactor\":\"SecondaryExposure\"}}");
        guidance.ReplaceAllocations(new[] { new PersistedContributionGuidanceAllocation(guidance.Id, "ExternalDiscovery:EXT11", "Brapi", "EXT11", "Externa", AssetClass.BrazilianStock, AssetType.Stock, "B3", "BRL", "Tecnologia", "trusted", 350m, "reason", 1, "[]") });
        db.AddRange(portfolio, guidance);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var item = (await new PortfolioHistoryService(db).GetTimelineAsync(portfolio.Id, 100, null, TestContext.Current.CancellationToken))!.Items.Single(x => x.Type == "DecisionMemory");
        Assert.Equal("EXT11", item.Ticker);
        Assert.Equal("AddNewAsset", item.DecisionType);
        Assert.Equal(350m, item.ContributionAmount);
        Assert.Equal("Recommended", item.ConfirmationStatus);
        Assert.Null(item.TransactionId);
        Assert.Empty(db.Assets);
    }

    [Fact]
    public async Task Legacy_guidance_history_is_readable_without_evidence()
    {
        await using var db = Db();
        var portfolio = new Portfolio("Teste");
        var asset = new Asset("OLD3", "Legado", AssetType.Stock, null);
        var guidance = new PersistedContributionGuidance(portfolio.Id, 100m, "p", "t", "i", "R4", "legacy", "legacy", "Legacy", "Registro antigo", "{}");
        guidance.ReplaceAllocations(new[] { new PersistedContributionGuidanceAllocation(guidance.Id, asset.Id, 100m, "OLD3", 1, "[]") });
        db.AddRange(portfolio, asset, guidance);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var item = (await new PortfolioHistoryService(db).GetTimelineAsync(portfolio.Id, 100, null, TestContext.Current.CancellationToken))!.Items.Single(x => x.Type == "DecisionMemory");
        Assert.Equal("OLD3", item.Ticker);
        Assert.Equal(100m, item.ContributionAmount);
        Assert.Empty(item.Evidence!);
    }

    [Fact]
    public async Task Split_confirmation_executes_both_allocations_once()
    {
        await using var db = Db();
        var portfolio = new Portfolio("Teste");
        var first = new Asset("ABCD3", "Primeiro", AssetType.Stock, null);
        var second = new Asset("XPTO11", "Segundo", AssetType.Fii, null);
        var guidance = new PersistedContributionGuidance(portfolio.Id, 1_000m, "p", "t", "i", "R16.7", "deterministic", "rules", "Deterministic", "reason", "{\"decisionType\":\"SplitContribution\"}");
        guidance.ReplaceAllocations(new[] { new PersistedContributionGuidanceAllocation(guidance.Id, first.Id, 650m, "first", 1, "[]"), new PersistedContributionGuidanceAllocation(guidance.Id, second.Id, 350m, "second", 1, "[]") });
        db.AddRange(portfolio, first, second, guidance);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new Prumo.Api.Controllers.PortfolioTransactionsController(db).Confirm(portfolio.Id, new ConfirmContributionRequest(guidance.Id, new[] { new ContributionPurchaseRequest(first.Id, 10, 65), new ContributionPurchaseRequest(second.Id, 10, 35) }), TestContext.Current.CancellationToken);
        Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result);
        var transaction = await db.PortfolioTransactions.Include(x => x.Items).SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(guidance.Id, transaction.ContributionGuidanceId);
        Assert.Equal(1_000m, transaction.TotalAmount);
        Assert.Equal(2, transaction.Items.Count);
        Assert.Equal(1, await db.PortfolioTransactions.CountAsync(x => x.ContributionGuidanceId == guidance.Id, cancellationToken: TestContext.Current.CancellationToken));
        var history = (await new PortfolioHistoryService(db).GetTimelineAsync(portfolio.Id, 100, null, TestContext.Current.CancellationToken))!.Items.Single(x => x.Type == "DecisionMemory");
        Assert.Equal("SplitContribution", history.DecisionType);
        Assert.Equal(new[] { "ABCD3", "XPTO11" }, history.AllocationTickers);
        Assert.Equal(new[] { 650m, 350m }, history.AllocationAmounts);
        Assert.Equal("Confirmed", history.ConfirmationStatus);
        var duplicate = await new PortfolioTransactionsController(db).Confirm(portfolio.Id, new ConfirmContributionRequest(guidance.Id, new[] { new ContributionPurchaseRequest(first.Id, 10, 65), new ContributionPurchaseRequest(second.Id, 10, 35) }), TestContext.Current.CancellationToken);
        Assert.IsType<Microsoft.AspNetCore.Mvc.ConflictObjectResult>(duplicate.Result);
        Assert.Equal(1, await db.PortfolioTransactions.CountAsync(x => x.ContributionGuidanceId == guidance.Id, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Confirmed_external_guidance_materializes_once_and_history_stays_coherent()
    {
        await using var db = Db();
        var portfolio = new Portfolio("Teste");
        var identity = CandidateIdentity.FromExternal(new("Brapi", "EXT11", "B3", AssetType.Stock));
        var guidance = new PersistedContributionGuidance(portfolio.Id, 100m, "p", "t", "i", "R16.7", "deterministic", "rules", "Deterministic", "reason", "{\"decisionType\":\"AddNewAsset\",\"decisionEvidence\":{\"decisiveFactor\":\"PrimaryStructuralImprovement\"}}");
        guidance.ReplaceAllocations(new[] { new PersistedContributionGuidanceAllocation(guidance.Id, identity.Key.Value, "Brapi", "EXT11", "Externa", AssetClass.BrazilianStock, AssetType.Stock, "B3", "BRL", null, "trusted", 100m, "reason", 1, "[]") });
        db.AddRange(portfolio, guidance);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Empty(db.Assets);

        var result = await new PortfolioTransactionsController(db).Confirm(portfolio.Id, new ConfirmContributionRequest(guidance.Id, new[] { new ContributionPurchaseRequest(Guid.Empty, 1, 100, identity.Key.Value) }), TestContext.Current.CancellationToken);
        Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result);
        Assert.Single(await db.Assets.Where(x => x.Ticker == "EXT11").ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
        var history = (await new PortfolioHistoryService(db).GetTimelineAsync(portfolio.Id, 100, null, TestContext.Current.CancellationToken))!.Items.Single(x => x.Type == "DecisionMemory");
        Assert.Equal(guidance.Id, history.RelatedEntityId);
        Assert.Equal("EXT11", history.Ticker);
        Assert.Equal("Confirmed", history.ConfirmationStatus);
        Assert.Equal("AddNewAsset", history.DecisionType);
        Assert.Contains("PrimaryStructuralImprovement", history.Evidence!);
        Assert.Single(await db.PortfolioTransactions.Where(x => x.ContributionGuidanceId == guidance.Id).ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
        var duplicate = await new PortfolioTransactionsController(db).Confirm(portfolio.Id, new ConfirmContributionRequest(guidance.Id, new[] { new ContributionPurchaseRequest(Guid.Empty, 1, 100, identity.Key.Value) }), TestContext.Current.CancellationToken);
        Assert.IsType<Microsoft.AspNetCore.Mvc.ConflictObjectResult>(duplicate.Result);
        Assert.Single(await db.Assets.Where(x => x.Ticker == "EXT11").ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task History_does_not_recompute_decision()
    {
        await using var db = Db();
        var portfolio = new Portfolio("Teste");
        var guidance = new PersistedContributionGuidance(portfolio.Id, 100m, "p", "t", "i", "R16.7", "deterministic", "rules", "Deterministic", "persisted", "{\"decisionType\":\"NoAction\",\"decisionEvidence\":{\"decisiveFactor\":\"StableTieBreaker\"}}");
        db.AddRange(portfolio, guidance);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var history = (await new PortfolioHistoryService(db).GetTimelineAsync(portfolio.Id, 100, null, TestContext.Current.CancellationToken))!.Items.Single(x => x.Type == "DecisionMemory");
        Assert.Equal("NoAction", history.DecisionType);
        Assert.Equal("Recommended", history.ConfirmationStatus);
        Assert.Equal("As alternativas ficaram equivalentes pelos critérios disponíveis.", history.Evidence!.Single());
    }

    private static PersistedContributionGuidance Guidance(Portfolio portfolio, string type, Asset asset, decimal amount)
    {
        var guidance = new PersistedContributionGuidance(portfolio.Id, amount, "p", "t", "i", "R16.7", "deterministic", "rules", "Deterministic", "reason", $"{{\"decisionType\":\"{type}\",\"decisionEvidence\":{{\"decisiveFactor\":\"Concentration\"}}}}");
        guidance.ReplaceAllocations(new[] { new PersistedContributionGuidanceAllocation(guidance.Id, asset.Id, amount, asset.Ticker, 1, "[]") });
        return guidance;
    }

    private static PrumoDbContext Db() => new(new DbContextOptionsBuilder<PrumoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
}
