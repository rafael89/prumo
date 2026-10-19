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

    private static PortfolioAssessment AddDocumentAssessment(PrumoDbContext db, Portfolio portfolio, Asset asset, ActionRequirement action, AssessmentStatus status)
    {
        var document = new Document(asset.Id, (DocumentType)0, DateTimeOffset.UtcNow, "Teste", null, "https://example.com/document", Guid.NewGuid().ToString());
        var analysis = new DocumentAnalysis(Guid.NewGuid(), document.Id, "test", "test-v1", AnalysisImportance.Relevant,
            AnalysisClassification.Other, action != ActionRequirement.None, SuggestedAction.Monitor,
            "Resumo", "Impacto do documento", "Impacto da carteira", [asset.Ticker], 1, "{}");
        var investmentEvent = new InvestmentEvent(analysis.Id, InvestmentEventType.Other, DateTimeOffset.UtcNow, "Evento", []);
        investmentEvent.AffectedAssets.Add(asset);
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
