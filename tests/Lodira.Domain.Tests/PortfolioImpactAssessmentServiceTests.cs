using Lodira.Application.Abstractions;
using Lodira.Application.Intelligence;
using Lodira.Application.Portfolio;
using Lodira.Api.Services;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lodira.Domain.Tests;

public sealed class PortfolioImpactAssessmentServiceTests
{
    [Fact]
    public void Small_exposure_without_effective_action_is_low()
    {
        var result = Assess(InvestmentEventType.Other, 2m);

        Assert.Equal(AttentionPriority.Low, result.AttentionPriority);
        Assert.Equal(ActionRequirement.None, result.ActionRequirement);
        Assert.Equal(CapitalDecisionImpact.None, result.CapitalDecisionImpact);
    }

    [Fact]
    public void Material_exposure_is_medium_and_monitored()
    {
        var result = Assess(InvestmentEventType.Other, 5m);

        Assert.Equal(AttentionPriority.Medium, result.AttentionPriority);
        Assert.Equal(ActionRequirement.Monitor, result.ActionRequirement);
    }

    [Fact]
    public void Small_corporate_action_without_material_exposure_is_low()
    {
        var result = Assess(InvestmentEventType.CorporateAction, 2m);

        Assert.Equal(AttentionPriority.Low, result.AttentionPriority);
        Assert.Equal(CapitalDecisionImpact.None, result.CapitalDecisionImpact);
    }

    [Fact]
    public void Large_exposure_thresholds_are_preserved()
    {
        Assert.Equal(AttentionPriority.High, Assess(InvestmentEventType.Other, 10m).AttentionPriority);
        Assert.Equal(AttentionPriority.Critical, Assess(InvestmentEventType.Other, 20m).AttentionPriority);
    }

    [Fact]
    public async Task Duplicate_stale_assessments_for_one_event_create_only_one_current_assessment()
    {
        var options = new DbContextOptionsBuilder<LodiraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new LodiraDbContext(options);
        var portfolio = new Portfolio("Carteira teste");
        var asset = new Asset("TEST", "Ativo teste", AssetType.Stock, "Teste");
        portfolio.Positions.Add(new PortfolioPosition(portfolio.Id, asset.Id, 1m, 1m, 1m));
        var investmentEvent = new InvestmentEvent(Guid.NewGuid(), InvestmentEventType.Other, DateTimeOffset.UtcNow, "Evento teste", Array.Empty<string>());
        investmentEvent.AffectedAssets.Add(asset);

        var first = CreateAssessment(portfolio.Id, investmentEvent, "portfolio-impact-v1");
        var second = CreateAssessment(portfolio.Id, investmentEvent, "portfolio-impact-v1");
        var service = new PortfolioAssessmentCurrentService(
            db,
            new PortfolioImpactAssessmentService(),
            new PortfolioAssessmentFreshnessEvaluator(),
            NullLogger<PortfolioAssessmentCurrentService>.Instance);

        var current = await service.EnsureFreshAsync(portfolio, Array.Empty<TargetAllocation>(), new[] { first, second }, CancellationToken.None);

        Assert.Single(current);
        Assert.Equal("portfolio-impact-v2", current[0].AssessmentVersion);
        Assert.Equal(1, await db.PortfolioAssessments.CountAsync());
    }

    private static PortfolioImpactAssessmentOutput Assess(InvestmentEventType eventType, decimal weight)
    {
        var portfolio = new Portfolio("Carteira teste");
        var asset = new Asset("TEST", "Ativo teste", AssetType.Stock, "Teste");
        var investmentEvent = new InvestmentEvent(Guid.NewGuid(), eventType, DateTimeOffset.UtcNow, "Evento teste", Array.Empty<string>());
        return new PortfolioImpactAssessmentService().AssessPortfolioImpact(new(
            investmentEvent,
            portfolio,
            new[] { new RelevantPositionContext(asset.Id, asset.Ticker, asset.AssetClass, weight, weight) },
            null));
    }

    private static PortfolioAssessment CreateAssessment(Guid portfolioId, InvestmentEvent investmentEvent, string version)
    {
        var assessment = new PortfolioAssessment(
            portfolioId,
            investmentEvent.Id,
            AttentionPriority.Medium,
            ActionRequirement.Monitor,
            CapitalDecisionImpact.None,
            AssessmentStatus.Detected,
            AssessmentUserState.Unread,
            "Avaliação antiga",
            "Motivo antigo",
            "Acompanhar",
            IntelligenceConfidence.Medium,
            null,
            version,
            "snapshot-antigo",
            investmentEvent.Version);
        assessment.AttachInvestmentEvent(investmentEvent);
        return assessment;
    }
}
