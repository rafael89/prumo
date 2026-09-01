using Lodira.Application.Abstractions;
using Lodira.Application.Portfolio;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Lodira.Api.Services;
public sealed class PortfolioAssessmentCurrentService
{
    private readonly LodiraDbContext db; private readonly IPortfolioImpactAssessmentService impactService; private readonly IPortfolioAssessmentFreshnessEvaluator freshnessEvaluator; private readonly ILogger<PortfolioAssessmentCurrentService> logger;
    public PortfolioAssessmentCurrentService(LodiraDbContext db,IPortfolioImpactAssessmentService impactService,IPortfolioAssessmentFreshnessEvaluator freshnessEvaluator,ILogger<PortfolioAssessmentCurrentService> logger){this.db=db;this.impactService=impactService;this.freshnessEvaluator=freshnessEvaluator;this.logger=logger;}
    public async Task<IReadOnlyList<PortfolioAssessment>> EnsureFreshAsync(Portfolio portfolio,IReadOnlyList<TargetAllocation> targets,IReadOnlyList<PortfolioAssessment> assessments,CancellationToken ct)
    {
        var snapshot=PortfolioSnapshotKeyCalculator.Calculate(portfolio); var targetKey=TargetAllocationKeyCalculator.Calculate(targets); var current=new List<PortfolioAssessment>(); var keys=new HashSet<string>();
        foreach(var assessment in assessments.Where(x=>x.Status!=AssessmentStatus.Closed)){
            var freshness=freshnessEvaluator.Evaluate(assessment,snapshot,targetKey,assessment.InvestmentEvent,impactService.Version); logger.LogInformation("PortfolioAssessment freshness evaluated PortfolioId={PortfolioId} AssessmentId={AssessmentId} FreshnessStatus={FreshnessStatus}",portfolio.Id,assessment.Id,freshness.Status);
            var key=$"{assessment.InvestmentEventId}|{snapshot}|{targetKey}|{impactService.Version}"; if(freshness.Status==FreshnessStatus.Fresh){current.Add(assessment);keys.Add(key);continue;} if(assessment.InvestmentEvent is null||!keys.Add(key))continue;
            var affected=assessment.InvestmentEvent.AffectedAssets.Select(x=>x.Id).ToHashSet(); var relevant=portfolio.Positions.Where(x=>affected.Contains(x.AssetId)).Select(x=>new RelevantPositionContext(x.AssetId,x.Asset?.Ticker??string.Empty,x.Asset?.AssetClass??AssetClass.Other,x.CurrentValue??0,x.PortfolioWeight)).ToList();
            var health=targetKey is null?null:new PortfolioHealthCalculator().Calculate(portfolio,targets); var output=impactService.AssessPortfolioImpact(new PortfolioImpactAssessmentContext(assessment.InvestmentEvent,portfolio,relevant,health));
            var replacement=new PortfolioAssessment(portfolio.Id,assessment.InvestmentEventId,output.AttentionPriority,output.ActionRequirement,output.CapitalDecisionImpact,AssessmentStatus.Detected,AssessmentUserState.Unread,output.Summary,output.WhyItMatters,output.SuggestedBehavior,output.Confidence,targetKey,impactService.Version,snapshot,assessment.InvestmentEvent.Version);
            replacement.AttachInvestmentEvent(assessment.InvestmentEvent);
            // The event was loaded with AsNoTracking and its asset graph can contain
            // different instances from the tracked portfolio assets. Track only the
            // new assessment so EF does not try to attach that graph again.
            db.Entry(replacement).State=EntityState.Added;
            current.Add(replacement);
        }
        if(current.Any(x=>db.Entry(x).State==EntityState.Added))await db.SaveChangesAsync(ct); return current;
    }
}
