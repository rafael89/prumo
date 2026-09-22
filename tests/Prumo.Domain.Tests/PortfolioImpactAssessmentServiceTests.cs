using Prumo.Application.Abstractions;
using Prumo.Application.Intelligence;
using Prumo.Application.Portfolio;
using Prumo.Api.Services;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Prumo.Domain.Tests;

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
    public void Relevant_held_position_below_threshold_is_low()
    {
        var result = Assess(InvestmentEventType.Other, 0.01m, AnalysisImportance.Relevant);

        Assert.Equal(AttentionPriority.Low, result.AttentionPriority);
        Assert.Equal(ActionRequirement.None, result.ActionRequirement);
    }

    [Fact]
    public void Relevant_event_requiring_action_is_reviewed_when_portfolio_has_exposure()
    {
        var result = Assess(InvestmentEventType.Other, 4.1m, AnalysisImportance.Relevant, requiresAction: true, action: SuggestedAction.Monitor);

        Assert.Equal(ActionRequirement.Review, result.ActionRequirement);
        Assert.Equal(AttentionPriority.Medium, result.AttentionPriority);
    }

    [Fact]
    public void Corporate_action_below_exposure_threshold_is_low()
    {
        var result = Assess(InvestmentEventType.CorporateAction, 0.01m);

        Assert.Equal(AttentionPriority.Low, result.AttentionPriority);
        Assert.Equal(CapitalDecisionImpact.None, result.CapitalDecisionImpact);
    }

    [Fact]
    public void Balanced_portfolio_marks_five_percent_asset_as_high()
    {
        var result = Assess(InvestmentEventType.Other, 5m);

        Assert.Equal(AttentionPriority.High, result.AttentionPriority);
        Assert.Equal(ActionRequirement.Review, result.ActionRequirement);
    }

    [Fact]
    public void Three_percent_asset_remains_low()
    {
        var result = Assess(InvestmentEventType.Other, 3m);

        Assert.Equal(AttentionPriority.Low, result.AttentionPriority);
    }

    [Fact]
    public void Ten_percent_asset_is_critical()
    {
        var result = Assess(InvestmentEventType.Other, 10m);

        Assert.Equal(AttentionPriority.Critical, result.AttentionPriority);
    }

    [Theory]
    [InlineData(2.99, AttentionPriority.Low)]
    [InlineData(3, AttentionPriority.Low)]
    [InlineData(5, AttentionPriority.Low)]
    [InlineData(10, AttentionPriority.Low)]
    public void Informative_no_action_does_not_become_attention_by_exposure(decimal weight, AttentionPriority expected)
    {
        var result = AssessInformative(InvestmentEventType.Distribution, weight);

        Assert.Equal(expected, result.AttentionPriority);
        Assert.Equal(ActionRequirement.None, result.ActionRequirement);
        Assert.Equal(CapitalDecisionImpact.None, result.CapitalDecisionImpact);
    }

    [Fact]
    public void Exceptional_distribution_keeps_normal_exposure_behavior()
    {
        var result = Assess(InvestmentEventType.Distribution, 6m, AnalysisImportance.Relevant, requiresAction: true, action: SuggestedAction.Monitor);

        Assert.Equal(AttentionPriority.High, result.AttentionPriority);
        Assert.Equal(ActionRequirement.Review, result.ActionRequirement);
    }

    [Fact]
    public void Relevant_capital_raise_is_not_suppressed()
    {
        var result = Assess(InvestmentEventType.CapitalRaise, 6m, AnalysisImportance.Relevant, requiresAction: true, action: SuggestedAction.Monitor);

        Assert.Equal(AttentionPriority.High, result.AttentionPriority);
        Assert.Equal(ActionRequirement.Review, result.ActionRequirement);
        Assert.Equal(CapitalDecisionImpact.Caution, result.CapitalDecisionImpact);
    }

    [Fact]
    public void Capital_raise_below_exposure_threshold_is_low()
    {
        var result = Assess(InvestmentEventType.CapitalRaise, 0.01m);

        Assert.Equal(AttentionPriority.Low, result.AttentionPriority);
        Assert.Equal(CapitalDecisionImpact.None, result.CapitalDecisionImpact);
    }

    [Fact]
    public void Critical_document_still_uses_exposure_thresholds()
    {
        var result = Assess(InvestmentEventType.Other, 0.01m, AnalysisImportance.Critical);

        Assert.Equal(AttentionPriority.Low, result.AttentionPriority);
        Assert.Equal(ActionRequirement.None, result.ActionRequirement);
        Assert.Equal(CapitalDecisionImpact.None, result.CapitalDecisionImpact);
    }

    [Fact]
    public void Liquidation_below_exposure_threshold_is_low()
    {
        var result = Assess(InvestmentEventType.Liquidation, 0.01m);
        Assert.Equal(AttentionPriority.Low, result.AttentionPriority);
        Assert.Equal(CapitalDecisionImpact.None, result.CapitalDecisionImpact);
    }

    [Fact]
    public async Task Duplicate_stale_assessments_for_one_event_create_only_one_current_assessment()
    {
        var options = new DbContextOptionsBuilder<PrumoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrumoDbContext(options);
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
        Assert.Equal("portfolio-impact-v6", current[0].AssessmentVersion);
        Assert.Equal(1, await db.PortfolioAssessments.CountAsync());
    }

    [Fact]
    public async Task Closed_document_stays_closed_when_target_allocation_changes()
    {
        var options = new DbContextOptionsBuilder<PrumoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrumoDbContext(options);
        var portfolio = new Portfolio("Carteira teste");
        var asset = new Asset("TEST", "Ativo teste", AssetType.Stock, "Teste");
        portfolio.Positions.Add(new PortfolioPosition(portfolio.Id, asset.Id, 1m, 1m, 1m));
        var investmentEvent = new InvestmentEvent(Guid.NewGuid(), InvestmentEventType.Other, DateTimeOffset.UtcNow, "Evento teste", Array.Empty<string>());
        investmentEvent.AffectedAssets.Add(asset);

        var oldTarget = new TargetAllocation(portfolio.Id, AssetClass.BrazilianStock, 100m);
        var newTarget = new TargetAllocation(portfolio.Id, AssetClass.FixedIncome, 100m);
        var closed = new PortfolioAssessment(
            portfolio.Id, investmentEvent.Id, AttentionPriority.High, ActionRequirement.Review,
            CapitalDecisionImpact.Caution, AssessmentStatus.Closed, AssessmentUserState.Seen,
            "Avaliação fechada", "Motivo", "Acompanhar", IntelligenceConfidence.High,
            TargetAllocationKeyCalculator.Calculate(new[] { oldTarget }), "portfolio-impact-v5",
            "snapshot-antigo", investmentEvent.Version);
        closed.AttachInvestmentEvent(investmentEvent);
        var stale = new PortfolioAssessment(
            portfolio.Id, investmentEvent.Id, AttentionPriority.High, ActionRequirement.Review,
            CapitalDecisionImpact.Caution, AssessmentStatus.Detected, AssessmentUserState.Seen,
            "Avaliação antiga", "Motivo", "Acompanhar", IntelligenceConfidence.High,
            TargetAllocationKeyCalculator.Calculate(new[] { oldTarget }), "portfolio-impact-v5",
            "snapshot-antigo", investmentEvent.Version);
        stale.AttachInvestmentEvent(investmentEvent);

        var service = new PortfolioAssessmentCurrentService(
            db,
            new PortfolioImpactAssessmentService(),
            new PortfolioAssessmentFreshnessEvaluator(),
            NullLogger<PortfolioAssessmentCurrentService>.Instance);

        var current = await service.EnsureFreshAsync(
            portfolio,
            new[] { newTarget },
            new[] { closed, stale },
            CancellationToken.None);

        var replacement = Assert.Single(current);
        Assert.Equal(AssessmentStatus.Closed, replacement.Status);
        Assert.Equal(AssessmentUserState.Seen, replacement.UserState);
    }

    [Fact]
    public async Task Seen_radar_item_does_not_become_unread_when_portfolio_changes()
    {
        var options = new DbContextOptionsBuilder<PrumoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrumoDbContext(options);
        var portfolio = new Portfolio("Carteira teste");
        var asset = new Asset("TEST", "Ativo teste", AssetType.Stock, "Teste");
        portfolio.Positions.Add(new PortfolioPosition(portfolio.Id, asset.Id, 1m, 1m, 1m));
        var investmentEvent = new InvestmentEvent(Guid.NewGuid(), InvestmentEventType.Other, DateTimeOffset.UtcNow, "Evento antigo", Array.Empty<string>());
        investmentEvent.AffectedAssets.Add(asset);

        var seen = new PortfolioAssessment(
            portfolio.Id, investmentEvent.Id, AttentionPriority.High, ActionRequirement.Review,
            CapitalDecisionImpact.Caution, AssessmentStatus.Detected, AssessmentUserState.Seen,
            "Avaliação já vista", "Motivo", "Acompanhar", IntelligenceConfidence.High,
            null, PortfolioImpactAssessmentService.CurrentVersion, "snapshot-antigo", investmentEvent.Version);
        seen.AttachInvestmentEvent(investmentEvent);

        var service = new PortfolioAssessmentCurrentService(
            db,
            new PortfolioImpactAssessmentService(),
            new PortfolioAssessmentFreshnessEvaluator(),
            NullLogger<PortfolioAssessmentCurrentService>.Instance);

        var current = await service.EnsureFreshAsync(portfolio, Array.Empty<TargetAllocation>(), new[] { seen }, CancellationToken.None);

        var replacement = Assert.Single(current);
        Assert.Equal(AssessmentStatus.Detected, replacement.Status);
        Assert.Equal(AssessmentUserState.Seen, replacement.UserState);
    }

    [Fact]
    public async Task Seen_duplicate_document_state_wins_when_stale_assessments_are_rebuilt()
    {
        var options = new DbContextOptionsBuilder<PrumoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrumoDbContext(options);
        var portfolio = new Portfolio("Carteira teste");
        var asset = new Asset("TEST", "Ativo teste", AssetType.Stock, "Teste");
        portfolio.Positions.Add(new PortfolioPosition(portfolio.Id, asset.Id, 1m, 1m, 1m));
        var investmentEvent = new InvestmentEvent(Guid.NewGuid(), InvestmentEventType.Other, DateTimeOffset.UtcNow, "Evento antigo", Array.Empty<string>());
        investmentEvent.AffectedAssets.Add(asset);

        var unread = CreateAssessment(portfolio.Id, investmentEvent, PortfolioImpactAssessmentService.CurrentVersion);
        var seen = new PortfolioAssessment(
            portfolio.Id, investmentEvent.Id, AttentionPriority.High, ActionRequirement.Review,
            CapitalDecisionImpact.Caution, AssessmentStatus.Detected, AssessmentUserState.Seen,
            "Avaliação duplicada já vista", "Motivo", "Acompanhar", IntelligenceConfidence.High,
            null, PortfolioImpactAssessmentService.CurrentVersion, "snapshot-antigo", investmentEvent.Version);
        seen.AttachInvestmentEvent(investmentEvent);

        var service = new PortfolioAssessmentCurrentService(
            db,
            new PortfolioImpactAssessmentService(),
            new PortfolioAssessmentFreshnessEvaluator(),
            NullLogger<PortfolioAssessmentCurrentService>.Instance);

        var current = await service.EnsureFreshAsync(portfolio, Array.Empty<TargetAllocation>(), new[] { unread, seen }, CancellationToken.None);

        var replacement = Assert.Single(current);
        Assert.Equal(AssessmentUserState.Seen, replacement.UserState);
        Assert.DoesNotContain(current, assessment => assessment.UserState == AssessmentUserState.Unread);
    }

    private static PortfolioImpactAssessmentOutput Assess(InvestmentEventType eventType, decimal weight, AnalysisImportance? importance = null, bool requiresAction = false, SuggestedAction action = SuggestedAction.Monitor)
    {
        var portfolio = new Portfolio("Carteira teste");
        var asset = new Asset("TEST", "Ativo teste", AssetType.Stock, "Teste");
        var investmentEvent = new InvestmentEvent(Guid.NewGuid(), eventType, DateTimeOffset.UtcNow, "Evento teste", Array.Empty<string>());
        if (importance is not null)
            typeof(InvestmentEvent).GetProperty(nameof(InvestmentEvent.DocumentAnalysis))!.SetValue(investmentEvent,
                new DocumentAnalysis(Guid.NewGuid(), Guid.NewGuid(), "test", "test-v1", importance.Value, AnalysisClassification.Other, requiresAction, action, "Resumo", "Impacto", "Impacto", new[] { asset.Ticker }, 1, "{}"));
        return new PortfolioImpactAssessmentService().AssessPortfolioImpact(new(
            investmentEvent,
            portfolio,
            new[] { new RelevantPositionContext(asset.Id, asset.Ticker, asset.AssetClass, weight, weight) },
            null,
            importance,
            requiresAction,
            action));
    }

    private static PortfolioImpactAssessmentOutput AssessInformative(InvestmentEventType eventType, decimal weight)
        => Assess(eventType, weight, AnalysisImportance.Informative, false, SuggestedAction.NoAction);

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
