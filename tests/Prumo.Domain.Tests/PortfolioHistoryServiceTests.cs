using Microsoft.EntityFrameworkCore;
using Prumo.Api.Services;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Domain.Tests;

public sealed class PortfolioHistoryServiceTests
{
    [Fact]
    public async Task Since_last_purchase_uses_latest_occurred_at_and_excludes_older_events()
    {
        await using var db = CreateDb();
        var seeded = await SeedAsync(db);
        AddPurchase(db, seeded.Portfolio.Id, seeded.Asset.Id, "2026-06-01", 1);
        AddPurchase(db, seeded.Portfolio.Id, seeded.Asset.Id, "2026-07-20", 1);
        AddPurchase(db, seeded.Portfolio.Id, seeded.Asset.Id, "2026-08-25", 1);
        AddEvent(db, seeded.Asset, "2026-08-10", "Evento X");
        AddEvent(db, seeded.Asset, "2026-08-27", "Evento Y");
        AddEvent(db, seeded.Asset, "2026-08-30", "Evento Z");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new PortfolioHistoryService(db).GetAssetHistoryAsync(seeded.Portfolio.Id, seeded.Asset.Id, 100, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(DateTimeOffset.Parse("2026-08-25T00:00:00Z"), result!.SinceLastPurchase.LastPurchaseAt);
        Assert.Equal(new[] { "Evento Z", "Evento Y" }, result.SinceLastPurchase.Items.Where(x => x.Type == "InvestmentEvent").Select(x => x.Summary));
        Assert.DoesNotContain(result.SinceLastPurchase.Items, x => x.Summary == "Evento X");
    }

    [Fact]
    public async Task Asset_without_purchase_returns_explicit_unknown_state()
    {
        await using var db = CreateDb();
        var seeded = await SeedAsync(db);
        AddEvent(db, seeded.Asset, "2026-08-27", "Evento sem compra");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new PortfolioHistoryService(db).GetAssetHistoryAsync(seeded.Portfolio.Id, seeded.Asset.Id, 100, CancellationToken.None);

        Assert.Equal("NoKnownPurchase", result!.SinceLastPurchase.Status);
        Assert.Null(result.SinceLastPurchase.LastPurchaseAt);
    }

    [Fact]
    public async Task Assessment_evolution_only_emits_real_changes_and_keeps_provenance()
    {
        await using var db = CreateDb();
        var seeded = await SeedAsync(db);
        var eventItem = AddEvent(db, seeded.Asset, "2026-08-20", "Evento que mudou a avaliação");
        AddAssessment(db, seeded.Portfolio.Id, eventItem, "2026-08-21", CapitalDecisionImpact.Favor);
        AddAssessment(db, seeded.Portfolio.Id, eventItem, "2026-08-22", CapitalDecisionImpact.Favor);
        AddAssessment(db, seeded.Portfolio.Id, eventItem, "2026-08-23", CapitalDecisionImpact.Caution);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new PortfolioHistoryService(db).GetAssetHistoryAsync(seeded.Portfolio.Id, seeded.Asset.Id, 100, CancellationToken.None);

        Assert.Equal(new string?[] { null, "Favor" }, result!.AssessmentEvolution.Select(x => x.PreviousAssessment));
        Assert.Equal(new[] { "Favor", "Caution" }, result.AssessmentEvolution.Select(x => x.CurrentAssessment));
        Assert.All(result.AssessmentEvolution, x => Assert.Equal(eventItem.Id, x.InvestmentEventId));
    }

    [Fact]
    public async Task History_isolated_by_portfolio_for_transactions_and_guidance()
    {
        await using var db = CreateDb();
        var first = await SeedAsync(db);
        var second = new Portfolio("Segunda");
        db.Portfolios.Add(second);
        db.PortfolioPositions.Add(new PortfolioPosition(second.Id, first.Asset.Id, 1, 10, 10));
        AddPurchase(db, first.Portfolio.Id, first.Asset.Id, "2026-08-01", 1);
        AddPurchase(db, second.Id, first.Asset.Id, "2026-08-02", 1);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new PortfolioHistoryService(db).GetAssetHistoryAsync(first.Portfolio.Id, first.Asset.Id, 100, CancellationToken.None);

        Assert.Single(result!.Items, x => x.Type == "Transaction");
        Assert.DoesNotContain(result.Items, x => x.OccurredAt == DateTimeOffset.Parse("2026-08-02T00:00:00Z"));
    }

    private static PrumoDbContext CreateDb() => new(new DbContextOptionsBuilder<PrumoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(Portfolio Portfolio, Asset Asset)> SeedAsync(PrumoDbContext db)
    {
        var portfolio = new Portfolio("Principal");
        var asset = new Asset("AAA3", "Ativo A", AssetType.Stock, "Tecnologia");
        db.Portfolios.Add(portfolio);
        db.Assets.Add(asset);
        db.PortfolioPositions.Add(new PortfolioPosition(portfolio.Id, asset.Id, 1, 10, 10));
        await db.SaveChangesAsync();
        return (portfolio, asset);
    }

    private static void AddPurchase(PrumoDbContext db, Guid portfolioId, Guid assetId, string date, decimal quantity)
    {
        var transaction = new PortfolioTransaction(portfolioId, PortfolioTransactionType.Buy, DateTimeOffset.Parse($"{date}T00:00:00Z"), null, quantity * 10);
        transaction.Items.Add(new PortfolioTransactionItem(transaction.Id, assetId, quantity, 10));
        db.PortfolioTransactions.Add(transaction);
    }

    private static InvestmentEvent AddEvent(PrumoDbContext db, Asset asset, string date, string summary)
    {
        var document = new Document(asset.Id, DocumentType.RelevantFact, DateTimeOffset.Parse($"{date}T00:00:00Z"), "CVM", $"doc-{summary}", $"https://source/{summary}", $"fp-{summary}");
        var analysis = new DocumentAnalysis(Guid.NewGuid(), document.Id, "test", "test-v1", AnalysisImportance.Relevant, AnalysisClassification.Other, false, SuggestedAction.Monitor, summary, "Impacto", "Impacto", new[] { asset.Ticker }, 1, "{}");
        var eventItem = new InvestmentEvent(analysis.Id, InvestmentEventType.Other, DateTimeOffset.Parse($"{date}T00:00:00Z"), summary, Array.Empty<string>());
        eventItem.AffectedAssets.Add(asset);
        db.Documents.Add(document);
        db.DocumentAnalyses.Add(analysis);
        db.InvestmentEvents.Add(eventItem);
        eventItem.Evidence.Add(new InvestmentEventEvidence(eventItem.Id, analysis.Id, document.Id, "Fonte de teste"));
        return eventItem;
    }

    private static void AddAssessment(PrumoDbContext db, Guid portfolioId, InvestmentEvent eventItem, string date, CapitalDecisionImpact impact)
    {
        var assessment = new PortfolioAssessment(portfolioId, eventItem.Id, AttentionPriority.Medium, ActionRequirement.Monitor, impact, AssessmentStatus.Detected, AssessmentUserState.Unread, eventItem.Summary, "Motivo", "Acompanhar", IntelligenceConfidence.Medium, null);
        assessment.AttachInvestmentEvent(eventItem);
        db.PortfolioAssessments.Add(assessment);
        db.Entry(assessment).Property(x => x.GeneratedAt).CurrentValue = DateTimeOffset.Parse($"{date}T00:00:00Z");
    }
}
