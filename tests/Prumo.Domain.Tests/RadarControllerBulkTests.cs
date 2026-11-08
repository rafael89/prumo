using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prumo.Api.Controllers;
using Prumo.Application.Intelligence;
using Prumo.Application.Portfolio;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Domain.Tests;

public sealed class RadarControllerBulkTests
{
    [Fact]
    public async Task Related_stock_documents_share_radar_dashboard_sources_and_actions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new PrumoDbContext(new DbContextOptionsBuilder<PrumoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var portfolio = new Portfolio("Carteira teste");
        var asset = new Asset("EGIE3", "Engie", AssetType.Stock, "Energia");
        portfolio.Positions.Add(new PortfolioPosition(portfolio.Id, asset.Id, 10m, 10m, 10m));
        db.AddRange(portfolio, asset);
        var first = AddDocumentAssessment(db, portfolio, asset, ActionRequirement.Review, AssessmentStatus.Active,
            "Convocação de assembleia para incorporação da ENGIE Brasil Energia Comercializadora Ltda.");
        var second = AddDocumentAssessment(db, portfolio, asset, ActionRequirement.Monitor, AssessmentStatus.Active,
            "Laudo de avaliação contábil para incorporação da ENGIE Brasil Energia Comercializadora Ltda.");
        var unrelated = AddDocumentAssessment(db, portfolio, asset, ActionRequirement.Review, AssessmentStatus.Active,
            "Divulgação de nova política de remuneração dos administradores.");
        var otherPortfolio = new Portfolio("Outra carteira");
        db.Add(otherPortfolio);
        var outside = AddDocumentAssessment(db, otherPortfolio, asset, ActionRequirement.Review, AssessmentStatus.Closed,
            "Convocação de assembleia para incorporação da ENGIE Brasil Energia Comercializadora Ltda.");
        await db.SaveChangesAsync(ct);
        var controller = new RadarController(db);

        var response = Assert.IsType<RadarResponse>(Assert.IsType<OkObjectResult>(
            (await controller.Get(portfolio.Id, null, null, null, ct: ct)).Result).Value);
        Assert.Equal(2, response.TotalItems);
        var card = Assert.Single(response.AttentionNow, x => x.Events!.Count == 2);
        Assert.Equal(2, card.Evidence.Select(x => x.DocumentId).Distinct().Count());
        var dashboard = Assert.IsType<DashboardResponse>(Assert.IsType<OkObjectResult>(
            (await new PortfoliosController(db).GetDashboard(portfolio.Id, cancellationToken: ct)).Result).Value);
        Assert.Equal(2, dashboard.AttentionNow!.Count);
        Assert.Single(dashboard.AttentionNow, x => x.Events!.Count == 2);

        await controller.DismissNotification(card.AssessmentId, portfolio.Id, ct);
        Assert.Equal(AssessmentUserState.Seen, first.UserState);
        Assert.Equal(AssessmentUserState.Seen, second.UserState);
        var notifications = Assert.IsType<RadarResponse>(Assert.IsType<OkObjectResult>(
            (await controller.Get(portfolio.Id, null, null, null, notificationPending: true, ct: ct)).Result).Value);
        Assert.Single(notifications.AttentionNow);
        await controller.SetStatus(card.AssessmentId, portfolio.Id, new("Closed"), ct);
        Assert.Equal(AssessmentStatus.Closed, first.Status);
        Assert.Equal(AssessmentStatus.Closed, second.Status);
        Assert.Equal(AssessmentStatus.Active, unrelated.Status);
        Assert.Equal(AssessmentUserState.Unread, unrelated.UserState);
        Assert.Equal(AssessmentUserState.Unread, outside.UserState);
        await controller.SetStatus(card.AssessmentId, portfolio.Id, new("Active"), ct);
        Assert.Equal(AssessmentStatus.Active, first.Status);
        Assert.Equal(AssessmentStatus.Active, second.Status);
        Assert.Equal(AssessmentStatus.Closed, outside.Status);
        await controller.ResolveAll(portfolio.Id, ct);
        Assert.Equal(AssessmentStatus.Closed, first.Status);
        Assert.Equal(AssessmentStatus.Closed, second.Status);
        var resolved = Assert.IsType<RadarResponse>(Assert.IsType<OkObjectResult>(
            (await controller.Get(portfolio.Id, null, null, null, ct: ct)).Result).Value);
        Assert.Equal(2, resolved.RecentUpdates.Count);
    }

    [Fact]
    public async Task Structured_events_share_one_document_card_and_its_actions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new PrumoDbContext(new DbContextOptionsBuilder<PrumoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var portfolio = new Portfolio("Carteira teste");
        var asset = new Asset("TRXF11", "TRX", AssetType.Stock, "Teste");
        portfolio.Positions.Add(new PortfolioPosition(portfolio.Id, asset.Id, 10m, 10m, 10m));
        db.AddRange(portfolio, asset);
        var first = AddDocumentAssessment(db, portfolio, asset, ActionRequirement.Review, AssessmentStatus.Closed);
        await db.SaveChangesAsync(ct);
        var firstEvent = await db.InvestmentEvents.SingleAsync(e => e.Id == first.InvestmentEventId, ct);
        firstEvent.SetRadarDetails(new(Importance: "Relevant"));
        var siblings = new[] { "Recompra de cotas", "Compra Meli City" }.Select(summary =>
        {
            var e = new InvestmentEvent(firstEvent.DocumentAnalysisId, InvestmentEventType.AssetTransaction, null, summary, []);
            e.SetRadarDetails(new(Importance: "Relevant"));
            e.AffectedAssets.Add(asset);
            var a = new PortfolioAssessment(portfolio.Id, e.Id, AttentionPriority.High, ActionRequirement.Review,
                CapitalDecisionImpact.None, AssessmentStatus.Closed, AssessmentUserState.Unread,
                summary, "Motivo", "Comportamento", IntelligenceConfidence.High, null,
                PortfolioImpactAssessmentService.CurrentVersion, PortfolioSnapshotKeyCalculator.Calculate(portfolio), e.Version);
            db.AddRange(e, a);
            return a;
        }).ToList();
        var unrelated = AddDocumentAssessment(db, portfolio, asset, ActionRequirement.Review, AssessmentStatus.Closed);
        var otherPortfolio = AddAssessment(db, Guid.NewGuid(), AnalysisImportance.Relevant, ActionRequirement.Review);
        await db.SaveChangesAsync(ct);
        var controller = new RadarController(db);

        var result = await controller.Get(portfolio.Id, null, null, null, ct: ct);
        var response = Assert.IsType<RadarResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(2, response.TotalItems);
        var card = Assert.Single(response.RecentUpdates, i => i.DocumentAnalysisId == firstEvent.DocumentAnalysisId);
        Assert.Equal(3, card.Events!.Count);
        Assert.Contains(card.Events, e => e.Summary == "Recompra de cotas");
        Assert.Contains(card.Events, e => e.Summary == "Compra Meli City");

        await controller.SetStatus(card.AssessmentId, portfolio.Id, new("Active"), ct);
        Assert.All(siblings.Append(first), a => Assert.Equal(AssessmentStatus.Active, a.Status));
        await controller.DismissNotification(card.AssessmentId, portfolio.Id, ct);
        Assert.All(siblings.Append(first), a => Assert.Equal(AssessmentUserState.Seen, a.UserState));
        await controller.SetStatus(card.AssessmentId, portfolio.Id, new("Closed"), ct);
        Assert.All(siblings.Append(first), a => Assert.Equal(AssessmentStatus.Closed, a.Status));
        Assert.Equal(AssessmentUserState.Unread, unrelated.UserState);
        Assert.Equal(AssessmentStatus.Detected, otherPortfolio.Status);
        Assert.Equal(AssessmentUserState.Unread, otherPortfolio.UserState);
    }

    [Fact]
    public async Task Resolve_all_keeps_monitoring_informative_events_and_other_portfolios_unchanged()
    {
        await using var db = new PrumoDbContext(new DbContextOptionsBuilder<PrumoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var portfolioId = Guid.NewGuid();
        var attention = AddAssessment(db, portfolioId, AnalysisImportance.Relevant, ActionRequirement.Review);
        var repeat = AddAssessment(db, portfolioId, AnalysisImportance.Informative, ActionRequirement.Monitor,
            new(Importance: "Relevant", Novelty: "Repeat"));
        var routine = AddAssessment(db, portfolioId, AnalysisImportance.Relevant, ActionRequirement.None,
            new(Importance: "Informative"));
        var otherPortfolio = AddAssessment(db, Guid.NewGuid(), AnalysisImportance.Informative, ActionRequirement.Monitor,
            new(Importance: "Relevant", Novelty: "Repeat"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new RadarController(db).ResolveAll(portfolioId, TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.IsType<RadarBulkUpdateResponse>(Assert.IsType<OkObjectResult>(result.Result).Value).UpdatedCount);
        Assert.Equal(AssessmentStatus.Closed, attention.Status);
        Assert.Equal(AssessmentStatus.Detected, repeat.Status);
        Assert.Equal(AssessmentStatus.Detected, routine.Status);
        Assert.Equal(AssessmentStatus.Detected, otherPortfolio.Status);
    }

    [Fact]
    public async Task Resolve_all_closes_action_only_radar_item_but_not_informative_document()
    {
        var options = new DbContextOptionsBuilder<PrumoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrumoDbContext(options);
        var portfolioId = Guid.NewGuid();
        var actionItem = AddAssessment(db, portfolioId, AnalysisImportance.Relevant, ActionRequirement.Review);
        var informativeItem = AddAssessment(db, portfolioId, AnalysisImportance.Informative, ActionRequirement.None);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new RadarController(db).ResolveAll(portfolioId, CancellationToken.None);

        Assert.Equal(1, Assert.IsType<RadarBulkUpdateResponse>(Assert.IsType<OkObjectResult>(result.Result).Value).UpdatedCount);
        Assert.Equal(AssessmentStatus.Closed, actionItem.Status);
        Assert.Equal(AssessmentStatus.Detected, informativeItem.Status);
    }

    [Fact]
    public async Task Reading_a_notification_does_not_close_its_radar_situation()
    {
        var options = new DbContextOptionsBuilder<PrumoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrumoDbContext(options);
        var portfolioId = Guid.NewGuid();
        var assessment = AddAssessment(db, portfolioId, AnalysisImportance.Relevant, ActionRequirement.Review);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new RadarController(db).DismissNotification(assessment.Id, portfolioId, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(AssessmentUserState.Seen, assessment.UserState);
        Assert.Equal(AssessmentStatus.Detected, assessment.Status);
    }

    [Fact]
    public async Task Radar_lists_only_attention_and_resolved_documents()
    {
        var options = new DbContextOptionsBuilder<PrumoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrumoDbContext(options);
        var portfolio = new Portfolio("Carteira teste");
        var asset = new Asset("TEST", "Ativo teste", AssetType.Stock, "Teste");
        portfolio.Positions.Add(new PortfolioPosition(portfolio.Id, asset.Id, 10m, 10m, 10m));
        db.AddRange(portfolio, asset);
        var attention = AddDocumentAssessment(db, portfolio, asset, ActionRequirement.Review, AssessmentStatus.Active);
        var resolved = AddDocumentAssessment(db, portfolio, asset, ActionRequirement.Review, AssessmentStatus.Closed);
        var monitoring = AddDocumentAssessment(db, portfolio, asset, ActionRequirement.Monitor, AssessmentStatus.Active);
        var other = AddDocumentAssessment(db, portfolio, asset, ActionRequirement.None, AssessmentStatus.Active);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new RadarController(db).Get(portfolio.Id, null, null, null, ct: TestContext.Current.CancellationToken);
        var response = Assert.IsType<RadarResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var listed = response.AttentionNow.Concat(response.RecentUpdates).Select(item => item.AssessmentId).ToList();

        Assert.All(response.AttentionNow.Concat(response.RecentUpdates), item => Assert.Equal("Impacto do documento", item.Impact));
        Assert.Equal(2, response.TotalItems);
        Assert.Contains(attention.Id, listed);
        Assert.Contains(resolved.Id, listed);
        Assert.DoesNotContain(other.Id, listed);
        Assert.DoesNotContain(monitoring.Id, listed);
        Assert.Single(response.AttentionNow);
        Assert.Single(response.RecentUpdates);
        Assert.Equal(1, response.RequiresAttention);
    }

    private static PortfolioAssessment AddDocumentAssessment(PrumoDbContext db, Portfolio portfolio, Asset asset, ActionRequirement action, AssessmentStatus status, string summary = "Evento")
    {
        var document = new Document(asset.Id, (DocumentType)0, DateTimeOffset.UtcNow, "Teste", null, "https://example.com/document", Guid.NewGuid().ToString());
        var analysis = new DocumentAnalysis(Guid.NewGuid(), document.Id, "test", "test-v1", AnalysisImportance.Relevant,
            AnalysisClassification.Other, action != ActionRequirement.None, SuggestedAction.Monitor,
            "Resumo", "Impacto do documento", "Impacto da carteira", [asset.Ticker], 1, "{}");
        var investmentEvent = new InvestmentEvent(analysis.Id, InvestmentEventType.CorporateAction, DateTimeOffset.UtcNow, summary, []);
        investmentEvent.AffectedAssets.Add(asset);
        investmentEvent.Evidence.Add(new InvestmentEventEvidence(investmentEvent.Id, analysis.Id, document.Id, summary));
        var assessment = new PortfolioAssessment(portfolio.Id, investmentEvent.Id, AttentionPriority.High, action,
            CapitalDecisionImpact.None, status, AssessmentUserState.Unread,
            "Resumo", "Motivo", "Comportamento", IntelligenceConfidence.High, null,
            PortfolioImpactAssessmentService.CurrentVersion, PortfolioSnapshotKeyCalculator.Calculate(portfolio), investmentEvent.Version);
        db.AddRange(document, analysis, investmentEvent, assessment);
        return assessment;
    }

    private static PortfolioAssessment AddAssessment(PrumoDbContext db, Guid portfolioId, AnalysisImportance importance, ActionRequirement action, RadarEventDetails? details = null)
    {
        var analysis = new DocumentAnalysis(Guid.NewGuid(), Guid.NewGuid(), "test", "test-v1", importance,
            AnalysisClassification.Other, action != ActionRequirement.None, SuggestedAction.Monitor,
            "Resumo", "Impacto", "Impacto", ["TEST"], 1, "{}");
        var investmentEvent = new InvestmentEvent(analysis.Id, InvestmentEventType.AssetTransaction,
            DateTimeOffset.UtcNow, "Evento", []);
        if (details is not null) investmentEvent.SetRadarDetails(details);
        var assessment = new PortfolioAssessment(portfolioId, investmentEvent.Id, AttentionPriority.High, action,
            CapitalDecisionImpact.None, AssessmentStatus.Detected, AssessmentUserState.Unread,
            "Resumo", "Motivo", "Comportamento", IntelligenceConfidence.High, null);
        db.AddRange(analysis, investmentEvent, assessment);
        return assessment;
    }
}
