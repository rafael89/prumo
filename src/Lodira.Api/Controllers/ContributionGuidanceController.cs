using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lodira.Application.Contributions;
using Lodira.Application.Portfolio;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Domain.Rules;
using Lodira.Infrastructure.Persistence;
using Lodira.Api.Services;

namespace Lodira.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/contribution-guidance")]
public sealed class ContributionGuidanceController(LodiraDbContext db, PortfolioAssessmentCurrentService current, IContributionIntelligenceProvider intelligence) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<object>> Generate(Guid portfolioId, ContributionRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0) return BadRequest("O valor do aporte deve ser positivo.");
        var portfolio = await db.Portfolios.Include(x => x.Positions).ThenInclude(x => x.Asset).SingleOrDefaultAsync(x => x.Id == portfolioId, ct);
        if (portfolio is null) return NotFound();
        var targets = await db.TargetAllocations.AsNoTracking().Where(x => x.PortfolioId == portfolioId).ToListAsync(ct);
        if (targets.Count == 0) return Conflict(new { status = ContributionGuidanceStatus.NotConfigured.ToString(), message = "Defina as alocações-alvo antes de simular um aporte." });
        if (portfolio.Positions.Any(x => !x.CurrentValue.HasValue)) return BadRequest("Não há preço válido para todas as posições existentes.");
        var snapshotKey = PortfolioSnapshotKeyCalculator.Calculate(portfolio); var targetKey = TargetAllocationKeyCalculator.Calculate(targets)!;
        var reusable = await db.ContributionGuidances.AsNoTracking().Include(x => x.Allocations).Where(x => x.PortfolioId == portfolioId && x.RequestedAmount == decimal.Round(request.Amount, 2) && x.PortfolioSnapshotKey == snapshotKey && x.TargetAllocationKey == targetKey).OrderByDescending(x => x.GeneratedAt).FirstOrDefaultAsync(ct);
        if (reusable is not null) return Ok(new { guidanceId = reusable.Id, requestedAmount = reusable.RequestedAmount, allocations = reusable.Allocations.Select(x => new { assetId = x.AssetId, amount = x.Amount, reason = x.Reason }), status = ContributionGuidanceStatus.Calculated.ToString(), intelligenceAvailability = reusable.IntelligenceAvailability, reasoning = reusable.Reasoning, generatedAt = reusable.GeneratedAt, reused = true });
        var health = new PortfolioHealthCalculator().Calculate(portfolio, targets); var total = portfolio.Positions.Sum(x => x.CurrentValue ?? 0);
        var positions = portfolio.Positions.Where(x => x.Asset is not null && x.CurrentValue.HasValue).Select(x => new GuidancePosition(x.AssetId, x.Asset!.Ticker, x.Asset.AssetClass, x.CurrentValue!.Value, total == 0 ? 0 : x.CurrentValue.Value / total * 100m)).ToList(); var ids = positions.Select(x => x.AssetId).ToHashSet();
        var assessments = await db.PortfolioAssessments.AsNoTracking().Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets).Include(x => x.InvestmentEvent!).ThenInclude(x => x.Evidence).Where(x => x.PortfolioId == portfolioId && x.Status != AssessmentStatus.Closed && x.InvestmentEvent!.AffectedAssets.Any(a => ids.Contains(a.Id))).ToListAsync(ct);
        assessments = (await current.EnsureFreshAsync(portfolio, targets, assessments, ct)).ToList();
        var inputs = assessments.Where(x => x.InvestmentEvent is not null).SelectMany(x => x.InvestmentEvent!.AffectedAssets.Where(a => ids.Contains(a.Id)).Select(a => new GuidanceAssessment(a.Id, x.CapitalDecisionImpact, x.Status, x.InvestmentEvent.Evidence.FirstOrDefault()?.Reference, x.AssessmentVersion, x.AttentionPriority, x.ActionRequirement, x.Summary, x.WhyItMatters, x.SuggestedBehavior, x.Confidence switch { IntelligenceConfidence.Low => .33m, IntelligenceConfidence.Medium => .66m, _ => 1m }))).ToList();
        var guidance = new ContributionGuidanceCalculator().Calculate(new ContributionGuidanceInput(portfolioId, request.Amount, health, positions, inputs));
        var deterministicBudgets = guidance.Allocations.GroupBy(x => x.AssetClass).ToDictionary(x => x.Key, x => x.Sum(a => a.Amount));
        var candidates = positions.Select(x => { var classPositions = positions.Where(p => p.AssetClass == x.AssetClass); var classTotal = classPositions.Sum(p => p.CurrentValue); var deviation = health.AllocationDeviations.FirstOrDefault(d => d.AssetClass == x.AssetClass); var contexts = assessments.Where(a => a.InvestmentEvent?.AffectedAssets.Any(a2 => a2.Id == x.AssetId) == true).Select(a => new ContributionAssessmentContext(a.AttentionPriority.ToString(), a.ActionRequirement.ToString(), a.CapitalDecisionImpact.ToString(), a.Summary, a.WhyItMatters, a.SuggestedBehavior, a.Confidence switch { IntelligenceConfidence.Low => .33m, IntelligenceConfidence.Medium => .66m, _ => 1m }, a.InvestmentEvent!.Evidence.Select(e => new Lodira.Application.Explainability.EvidenceReference("event", e.Reference, e.Reference)).ToArray())).ToArray(); return new ContributionCandidate(x.AssetId, x.Ticker, x.AssetClass, x.CurrentValue, x.CurrentWeight, total == 0 ? 0 : classTotal / total * 100m, deviation?.TargetPercentage ?? 0, deterministicBudgets.GetValueOrDefault(x.AssetClass), inputs.Any(a => a.AssetId == x.AssetId && a.Impact == CapitalDecisionImpact.Block), contexts); }).ToList();
        var availability = "InsufficientIntelligence"; var providerName = "deterministic"; var modelName = "none"; var reasoning = string.Join(" ", guidance.Reasons);
        try
        {
            var ai = await intelligence.EvaluateAsync(portfolioId, request.Amount, candidates, ct);
            if (ContributionIntelligenceValidator.IsValid(ai, request.Amount, candidates) && ai.Availability != ContributionIntelligenceAvailability.InsufficientIntelligence)
            {
                guidance = ContributionIntelligenceApplier.Apply(guidance, ai, positions);
                availability = ai.Availability.ToString(); providerName = ai.Metadata.Provider; modelName = ai.Metadata.Model; reasoning = ai.OverallReasoning;
            }
        }
        catch (Exception ex) { HttpContext.RequestServices.GetRequiredService<ILogger<ContributionGuidanceController>>().LogWarning("Contribution Intelligence indisponível; usando fallback determinístico. Tipo={ExceptionType}", ex.GetType().Name); }
        var persisted = new PersistedContributionGuidance(portfolioId, guidance.RequestedAmount, snapshotKey, targetKey, "R4-2.0", providerName, modelName, availability, reasoning, JsonSerializer.Serialize(new { guidance.AllocationStyle, guidance.Status }));
        foreach (var allocation in guidance.Allocations) persisted.Allocations.Add(new PersistedContributionGuidanceAllocation(persisted.Id, allocation.AssetId, allocation.Amount, allocation.Reason, 0, JsonSerializer.Serialize(allocation.Evidence)));
        db.ContributionGuidances.Add(persisted); await db.SaveChangesAsync(ct);
        return Ok(new { guidanceId = persisted.Id, status = guidance.Status.ToString(), contributionAmount = guidance.ContributionAmount, requestedAmount = guidance.RequestedAmount, allocatedAmount = guidance.AllocatedAmount, unallocatedAmount = guidance.UnallocatedAmount, allocationStyle = guidance.AllocationStyle.ToString(), allocations = guidance.Allocations, avoidedCandidates = guidance.AvoidedCandidates, reasons = guidance.Reasons, evidence = guidance.Evidence, intelligenceAvailability = persisted.IntelligenceAvailability, reasoning = persisted.Reasoning, reused = false });
    }

    [HttpGet("{guidanceId:guid}")]
    public async Task<ActionResult<object>> Get(Guid portfolioId, Guid guidanceId, CancellationToken ct)
    { var item = await db.ContributionGuidances.AsNoTracking().Include(x => x.Allocations).SingleOrDefaultAsync(x => x.Id == guidanceId && x.PortfolioId == portfolioId, ct); return item is null ? NotFound() : Ok(item); }
}

public sealed record ContributionRequest(decimal Amount);
