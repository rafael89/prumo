using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prumo.Api.Controllers;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Domain.Tests;

public sealed class RadarControllerBulkTests
{
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
        await db.SaveChangesAsync();

        var result = await new RadarController(db).ResolveAll(portfolioId, CancellationToken.None);

        Assert.Equal(1, Assert.IsType<RadarBulkUpdateResponse>(Assert.IsType<OkObjectResult>(result.Result).Value).UpdatedCount);
        Assert.Equal(AssessmentStatus.Closed, actionItem.Status);
        Assert.Equal(AssessmentStatus.Detected, informativeItem.Status);
    }

    private static PortfolioAssessment AddAssessment(PrumoDbContext db, Guid portfolioId, AnalysisImportance importance, ActionRequirement action)
    {
        var analysis = new DocumentAnalysis(Guid.NewGuid(), Guid.NewGuid(), "test", "test-v1", importance,
            AnalysisClassification.Other, action != ActionRequirement.None, SuggestedAction.Monitor,
            "Resumo", "Impacto", "Impacto", ["TEST"], 1, "{}");
        var investmentEvent = new InvestmentEvent(analysis.Id, InvestmentEventType.AssetTransaction,
            DateTimeOffset.UtcNow, "Evento", []);
        var assessment = new PortfolioAssessment(portfolioId, investmentEvent.Id, AttentionPriority.High, action,
            CapitalDecisionImpact.None, AssessmentStatus.Detected, AssessmentUserState.Unread,
            "Resumo", "Motivo", "Comportamento", IntelligenceConfidence.High, null);
        db.AddRange(analysis, investmentEvent, assessment);
        return assessment;
    }
}
