using Lodira.Api.Controllers;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lodira.Domain.Tests;

public sealed class DashboardRegressionTests
{
    [Fact]
    public async Task Dashboard_returns_one_recent_news_item_per_investment_event()
    {
        var options = new DbContextOptionsBuilder<LodiraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new LodiraDbContext(options);
        var portfolio = new Portfolio("Carteira teste");
        var asset = new Asset("TEST", "Ativo teste", AssetType.Stock, "Teste");
        portfolio.Positions.Add(new PortfolioPosition(portfolio.Id, asset.Id, 10m, 10m, 10m));
        var investmentEvent = new InvestmentEvent(Guid.NewGuid(), InvestmentEventType.Other, DateTimeOffset.UtcNow, "Notícia repetida", Array.Empty<string>());
        investmentEvent.AffectedAssets.Add(asset);

        db.AddRange(portfolio, asset, investmentEvent,
            CreateAssessment(portfolio.Id, investmentEvent, "portfolio-impact-v1"),
            CreateAssessment(portfolio.Id, investmentEvent, "portfolio-impact-v1"));
        await db.SaveChangesAsync();

        var result = await new PortfoliosController(db).GetDashboard(portfolio.Id, cancellationToken: CancellationToken.None);
        var response = Assert.IsType<OkObjectResult>(result.Result);
        var dashboard = Assert.IsType<DashboardResponse>(response.Value);

        Assert.Single(dashboard.RecentUpdates!);
        Assert.Equal(investmentEvent.Id, dashboard.RecentUpdates![0].InvestmentEventId);
    }

    private static PortfolioAssessment CreateAssessment(Guid portfolioId, InvestmentEvent investmentEvent, string version)
    {
        var assessment = new PortfolioAssessment(portfolioId, investmentEvent.Id, AttentionPriority.Low, ActionRequirement.None, CapitalDecisionImpact.None, AssessmentStatus.Detected, AssessmentUserState.Unread, "Resumo", "Motivo", "Nenhuma ação necessária", IntelligenceConfidence.Medium, null, version, "snapshot-antigo", investmentEvent.Version);
        assessment.AttachInvestmentEvent(investmentEvent);
        return assessment;
    }
}
