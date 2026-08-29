using Lodira.Application.Abstractions;
using Lodira.Application.Portfolio;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lodira.Api.Services;

public sealed class PortfolioAssessmentCurrentService
{
    private readonly LodiraDbContext db;
    private readonly IPortfolioImpactAssessmentService impactService;
    private readonly IPortfolioAssessmentFreshnessEvaluator freshnessEvaluator;
    private readonly ILogger<PortfolioAssessmentCurrentService> logger;

    public PortfolioAssessmentCurrentService(LodiraDbContext db, IPortfolioImpactAssessmentService impactService, IPortfolioAssessmentFreshnessEvaluator freshnessEvaluator, ILogger<PortfolioAssessmentCurrentService> logger)
    {
        this.db = db; this.impactService = impactService; this.freshnessEvaluator = freshnessEvaluator; this.logger = logger;
    }

    public async Task<IReadOnlyList<PortfolioAssessment>> EnsureFreshAsync(Portfolio portfolio, PortfolioAllocationStrategy? strategy, InvestmentObjective? objective, IReadOnlyList<PortfolioAssessment> assessments, CancellationToken ct)
    {
        var snapshot = PortfolioSnapshotKeyCalculator.Calculate(portfolio);
        var current = new List<PortfolioAssessment>();
        var createdKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assessment in assessments.Where(x => x.Status != AssessmentStatus.Closed))
        {
            var freshness = freshnessEvaluator.Evaluate(assessment, snapshot, strategy?.Version, objective?.Version, assessment.InvestmentEvent, impactService.Version);
            logger.LogInformation("PortfolioAssessment freshness evaluated PortfolioId={PortfolioId} AssessmentId={AssessmentId} FreshnessStatus={FreshnessStatus}", portfolio.Id, assessment.Id, freshness.Status);
            var key = $"{assessment.InvestmentEventId}|{snapshot}|{strategy?.Version ?? 0}|{objective?.Version ?? 0}|{impactService.Version}";
            if (freshness.Status == FreshnessStatus.Fresh) { current.Add(assessment); createdKeys.Add(key); continue; }
            if (assessment.InvestmentEvent is null) continue;
            if (!createdKeys.Add(key)) continue;
            var affectedIds = assessment.InvestmentEvent.AffectedAssets.Select(x => x.Id).ToHashSet();
            var relevant = portfolio.Positions.Where(x => affectedIds.Contains(x.AssetId)).Select(x => new RelevantPositionContext(x.AssetId, x.Asset?.Ticker ?? string.Empty, x.Asset?.AssetClass ?? AssetClass.Other, x.CurrentValue ?? 0, x.PortfolioWeight)).ToList();
            var health = strategy is null ? null : new PortfolioHealthCalculator().Calculate(portfolio, strategy);
            var output = impactService.AssessPortfolioImpact(new PortfolioImpactAssessmentContext(assessment.InvestmentEvent, portfolio, relevant, objective, strategy, health));
            var replacement = new PortfolioAssessment(portfolio.Id, assessment.InvestmentEventId, output.AttentionPriority, output.ActionRequirement, output.CapitalDecisionImpact, AssessmentStatus.Detected, AssessmentUserState.Unread, output.Summary, output.WhyItMatters, output.SuggestedBehavior, output.Confidence, strategy?.Version, objective?.Version, impactService.Version, snapshot, assessment.InvestmentEvent.Version);
            replacement.AttachInvestmentEvent(assessment.InvestmentEvent);
            db.PortfolioAssessments.Add(replacement); current.Add(replacement);
            logger.LogInformation("PortfolioAssessment reevaluation requested PortfolioId={PortfolioId} PortfolioSnapshotKey={PortfolioSnapshotKey} AssessmentVersion={AssessmentVersion} InvestmentEventId={InvestmentEventId} InvestmentEventVersion={InvestmentEventVersion}", portfolio.Id, snapshot, impactService.Version, assessment.InvestmentEventId, assessment.InvestmentEvent.Version);
        }
        if (current.Any(x => db.Entry(x).State == EntityState.Added)) await db.SaveChangesAsync(ct);
        return current;
    }
}
