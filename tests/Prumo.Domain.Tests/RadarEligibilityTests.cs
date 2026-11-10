using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prumo.Api.Controllers;
using Prumo.Api.Services;
using Prumo.Application.Intelligence;
using Prumo.Application.Portfolio;
using Prumo.Application.Radar;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Domain.Rules;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Domain.Tests;

public sealed class RadarEligibilityTests
{
    [Theory]
    [InlineData(InvestmentEventType.CapitalRaise, AnalysisClassification.CapitalRaise, null)]
    [InlineData(InvestmentEventType.Other, AnalysisClassification.CapitalRaise, null)]
    [InlineData(InvestmentEventType.Operational, AnalysisClassification.CapitalRaise, null)]
    [InlineData(InvestmentEventType.CorporateAction, AnalysisClassification.Other, "Instrumento Particular de Emissão de Cotas")]
    [InlineData(InvestmentEventType.Other, AnalysisClassification.Other, "Anúncio de Encerramento")]
    [InlineData(InvestmentEventType.Other, AnalysisClassification.Other, "Prospecto Definitivo")]
    public async Task Existing_offerings_disappear_from_radar_notifications_dashboard_and_contribution_context(
        InvestmentEventType eventType, AnalysisClassification classification, string? sourceType)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var portfolio = new Portfolio("Carteira");
        var asset = new Asset("TEST11", "Fundo", AssetType.Fii, "Logística");
        portfolio.Positions.Add(new(portfolio.Id, asset.Id, 10, 100, 100));
        PortfolioCalculator.RecalculateWeights(portfolio.Positions);
        db.AddRange(portfolio, asset);
        var document = new Document(asset.Id, DocumentType.RelevantFact, DateTimeOffset.UtcNow,
            "CVM", "oferta", "https://example.test/oferta", "oferta", sourceType: sourceType);
        var analysis = new DocumentAnalysis(Guid.NewGuid(), document.Id, "test", "old-prompt", AnalysisImportance.Critical,
            classification, true, SuggestedAction.Monitor, "Oferta", "Oferta", "Oferta", [asset.Ticker], 1, "{}");
        var investmentEvent = new InvestmentEvent(analysis.Id, eventType, DateTimeOffset.UtcNow, "Oferta", []);
        investmentEvent.AffectedAssets.Add(asset);
        investmentEvent.SetRadarDetails(new(Importance: "Critical", AttentionBasis: "DEADLINE",
            AttentionReason: "Subscrição", AttentionEvidence: "Prazo de subscrição"));
        AddAssessment(db, portfolio, investmentEvent);
        db.AddRange(document, analysis, investmentEvent);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var radar = new RadarController(db);
        var response = Assert.IsType<RadarResponse>(Assert.IsType<OkObjectResult>(
            (await radar.Get(portfolio.Id, null, null, null, ct: ct)).Result).Value);
        Assert.Equal(0, response.TotalItems);
        Assert.Equal(0, response.RequiresAttention);
        var notifications = Assert.IsType<RadarResponse>(Assert.IsType<OkObjectResult>(
            (await radar.Get(portfolio.Id, null, null, null, notificationPending: true, ct: ct)).Result).Value);
        Assert.Equal(0, notifications.TotalItems);
        var dashboard = Assert.IsType<DashboardResponse>(Assert.IsType<OkObjectResult>(
            (await new PortfoliosController(db).GetDashboard(portfolio.Id, cancellationToken: ct)).Result).Value);
        Assert.Empty(dashboard.AttentionNow!);

        var loaded = await db.PortfolioAssessments.Include(x => x.InvestmentEvent!).ThenInclude(x => x.DocumentAnalysis!)
            .ThenInclude(x => x.Document).SingleAsync(ct);
        Assert.False(RadarAttentionNowPolicy.RequiresAttention(loaded));
        var current = new PortfolioAssessmentCurrentService(db, new PortfolioImpactAssessmentService(),
            new PortfolioAssessmentFreshnessEvaluator(), NullLogger<PortfolioAssessmentCurrentService>.Instance);
        Assert.Empty(await current.EnsureFreshAsync(portfolio, [], [loaded], ct));
        loaded.SetStatus(AssessmentStatus.Closed);
        await db.SaveChangesAsync(ct);
        var closed = Assert.IsType<RadarResponse>(Assert.IsType<OkObjectResult>(
            (await radar.Get(portfolio.Id, null, null, null, ct: ct)).Result).Value);
        Assert.Empty(closed.RecentUpdates);
        Assert.Equal(1, await db.DocumentAnalyses.CountAsync(ct));
        Assert.Equal(1, await db.PortfolioAssessments.CountAsync(ct));
    }

    [Fact]
    public async Task Mixed_document_keeps_independent_material_event_and_hides_issuance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var portfolio = new Portfolio("Carteira");
        var asset = new Asset("TEST11", "Fundo", AssetType.Fii, "Logística");
        portfolio.Positions.Add(new(portfolio.Id, asset.Id, 10, 100, 100));
        PortfolioCalculator.RecalculateWeights(portfolio.Positions);
        db.AddRange(portfolio, asset);
        var document = new Document(asset.Id, DocumentType.RelevantFact, DateTimeOffset.UtcNow,
            "CVM", "misto", "https://example.test/misto", "misto");
        var analysis = new DocumentAnalysis(Guid.NewGuid(), document.Id, "test", "old-prompt", AnalysisImportance.Relevant,
            AnalysisClassification.CapitalRaise, true, SuggestedAction.Monitor, "Emissão e aquisição", "Impacto", "Impacto", [asset.Ticker], 1, "{}");
        var issuance = new InvestmentEvent(analysis.Id, InvestmentEventType.CapitalRaise, null, "Emissão", []);
        var acquisition = new InvestmentEvent(analysis.Id, InvestmentEventType.AssetTransaction, null, "Aquisição de imóvel", []);
        foreach (var e in new[] { issuance, acquisition })
        {
            e.AffectedAssets.Add(asset);
            AddAssessment(db, portfolio, e);
        }
        db.AddRange(document, analysis, issuance, acquisition);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var response = Assert.IsType<RadarResponse>(Assert.IsType<OkObjectResult>(
            (await new RadarController(db).Get(portfolio.Id, null, null, null, ct: ct)).Result).Value);

        var card = Assert.Single(response.AttentionNow);
        Assert.Equal(InvestmentEventType.AssetTransaction, card.EventType);
        Assert.Equal(InvestmentEventType.AssetTransaction, Assert.Single(card.Events!).EventType);
        Assert.Equal(1, response.RequiresAttention);
        Assert.Equal(2, await db.InvestmentEvents.CountAsync(ct));
    }

    private static PrumoDbContext CreateDb() => new(new DbContextOptionsBuilder<PrumoDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static PortfolioAssessment AddAssessment(PrumoDbContext db, Portfolio portfolio, InvestmentEvent investmentEvent)
    {
        var assessment = new PortfolioAssessment(portfolio.Id, investmentEvent.Id, AttentionPriority.Critical, ActionRequirement.Review,
            CapitalDecisionImpact.Caution, AssessmentStatus.Active, AssessmentUserState.Unread,
            investmentEvent.Summary, "Motivo", "Revisar", IntelligenceConfidence.High, null,
            PortfolioImpactAssessmentService.CurrentVersion, PortfolioSnapshotKeyCalculator.Calculate(portfolio), investmentEvent.Version);
        db.Add(assessment);
        return assessment;
    }
}
