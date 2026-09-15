using Prumo.Application.Intelligence;
using Prumo.Application.Portfolio;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Prumo.Api.Controllers;

namespace Prumo.Domain.Tests;

public sealed class DashboardRegressionTests
{
    [Fact]
    public async Task Dashboard_ignores_assessment_when_analysis_document_is_missing()
    {
        var options = new DbContextOptionsBuilder<PrumoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrumoDbContext(options);
        var portfolio = new Portfolio("Carteira sem documento");
        var investmentEvent = new InvestmentEvent(Guid.NewGuid(), InvestmentEventType.Other, DateTimeOffset.UtcNow, "Evento legado", Array.Empty<string>());
        db.AddRange(portfolio, investmentEvent, CreateAssessment(portfolio.Id, investmentEvent, "portfolio-impact-v1"));
        await db.SaveChangesAsync();

        var result = await new Prumo.Api.Controllers.PortfoliosController(db).GetDashboard(portfolio.Id);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        Assert.IsType<DashboardResponse>(response.Value);
    }

    [Fact]
    public async Task Current_assessments_deduplicate_one_event()
    {
        var options = new DbContextOptionsBuilder<PrumoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrumoDbContext(options);
        var portfolio = new Portfolio("Carteira teste");
        var asset = new Asset("TEST", "Ativo teste", AssetType.Stock, "Teste");
        portfolio.Positions.Add(new PortfolioPosition(portfolio.Id, asset.Id, 10m, 10m, 10m));
        var investmentEvent = new InvestmentEvent(Guid.NewGuid(), InvestmentEventType.Other, DateTimeOffset.UtcNow, "Notícia repetida", Array.Empty<string>());
        investmentEvent.AffectedAssets.Add(asset);
        db.AddRange(portfolio, asset, investmentEvent);
        await db.SaveChangesAsync();
        db.AddRange(
            CreateAssessment(portfolio.Id, investmentEvent, "portfolio-impact-v1"),
            CreateAssessment(portfolio.Id, investmentEvent, "portfolio-impact-v1"));
        await db.SaveChangesAsync();

        var loaded = await db.PortfolioAssessments.AsNoTracking().Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets).ToListAsync();
        var current = await new Prumo.Api.Services.PortfolioAssessmentCurrentService(db, new PortfolioImpactAssessmentService(), new PortfolioAssessmentFreshnessEvaluator(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Prumo.Api.Services.PortfolioAssessmentCurrentService>.Instance)
            .EnsureFreshAsync(portfolio, Array.Empty<TargetAllocation>(), loaded, CancellationToken.None);

        Assert.Single(current);
        Assert.Equal(PortfolioImpactAssessmentService.CurrentVersion, current[0].AssessmentVersion);
    }

    private static PortfolioAssessment CreateAssessment(Guid portfolioId, InvestmentEvent investmentEvent, string version)
    {
        var assessment = new PortfolioAssessment(portfolioId, investmentEvent.Id, AttentionPriority.Low, ActionRequirement.None, CapitalDecisionImpact.None, AssessmentStatus.Detected, AssessmentUserState.Unread, "Resumo", "Motivo", "Nenhuma ação necessária", IntelligenceConfidence.Medium, null, version, "snapshot-antigo", investmentEvent.Version);
        assessment.AttachInvestmentEvent(investmentEvent);
        return assessment;
    }
}
