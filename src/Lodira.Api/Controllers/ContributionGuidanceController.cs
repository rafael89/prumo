using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lodira.Application.Contributions;
using Lodira.Application.Portfolio;
using Lodira.Api.Services;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Domain.Rules;
using Lodira.Infrastructure.Persistence;

namespace Lodira.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/contribution-guidance")]
public sealed class ContributionGuidanceController(LodiraDbContext db, PortfolioAssessmentCurrentService current) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<object>> Generate(Guid portfolioId, ContributionRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0) return BadRequest("O valor do aporte deve ser positivo.");
        var portfolio = await db.Portfolios.Include(x => x.Positions).ThenInclude(x => x.Asset).SingleOrDefaultAsync(x => x.Id == portfolioId, ct);
        if (portfolio is null) return NotFound();
        var targets = await db.TargetAllocations.AsNoTracking().Where(x => x.PortfolioId == portfolioId).ToListAsync(ct);
        if (targets.Count == 0) return Conflict(new { status = ContributionGuidanceStatus.NotConfigured.ToString() });
        if (portfolio.Positions.Any(x => !x.CurrentValue.HasValue)) return BadRequest("Não há preço válido para todas as posições existentes.");
        var snapshot = PortfolioSnapshotKeyCalculator.Calculate(portfolio); var targetKey = TargetAllocationKeyCalculator.Calculate(targets)!; var amount = decimal.Round(request.Amount, 2);
        var existing = await db.ContributionGuidances.AsNoTracking().Include(x => x.Allocations).Where(x => x.PortfolioId == portfolioId && x.RequestedAmount == amount && x.PortfolioSnapshotKey == snapshot && x.TargetAllocationKey == targetKey).OrderByDescending(x => x.GeneratedAt).FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            var positionsByAsset = await db.PortfolioPositions.AsNoTracking().Include(x => x.Asset).Where(x => x.PortfolioId == portfolioId).ToDictionaryAsync(x => x.AssetId, ct);
            var allocations = existing.Allocations.Select(allocation => new
            {
                assetId = allocation.AssetId,
                ticker = positionsByAsset.GetValueOrDefault(allocation.AssetId)?.Asset?.Ticker ?? string.Empty,
                amount = allocation.Amount,
                reason = allocation.Reason,
                evidence = Array.Empty<string>()
            }).ToArray();
            return Ok(new
            {
                guidanceId = existing.Id,
                status = "QueuedOrCompleted",
                contributionAmount = existing.RequestedAmount,
                requestedAmount = existing.RequestedAmount,
                allocatedAmount = allocations.Sum(x => x.amount),
                unallocatedAmount = existing.RequestedAmount - allocations.Sum(x => x.amount),
                allocationStyle = allocations.Length == 1 ? "Concentrated" : allocations.Length > 1 ? "Distributed" : "NoAllocation",
                allocations,
                avoidedCandidates = Array.Empty<object>(),
                reasons = new[] { existing.Reasoning },
                evidence = Array.Empty<string>(),
                intelligenceAvailability = existing.IntelligenceAvailability,
                jobStatus = await db.ContributionGuidanceJobs.Where(x => x.ContributionGuidanceId == existing.Id).Select(x => x.Status.ToString()).FirstOrDefaultAsync(ct),
                reused = true
            });
        }
        var health = new PortfolioHealthCalculator().Calculate(portfolio, targets); var total = portfolio.Positions.Sum(x => x.CurrentValue ?? 0); var positions = portfolio.Positions.Where(x => x.Asset is not null && x.CurrentValue.HasValue).Select(x => new GuidancePosition(x.AssetId, x.Asset!.Ticker, x.Asset.AssetClass, x.CurrentValue!.Value, total == 0 ? 0 : x.CurrentValue.Value / total * 100m)).ToList(); var ids = positions.Select(x => x.AssetId).ToHashSet();
        var assessments = await db.PortfolioAssessments.AsNoTracking().Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets).Include(x => x.InvestmentEvent!).ThenInclude(x => x.Evidence).Where(x => x.PortfolioId == portfolioId && x.Status != AssessmentStatus.Closed && x.InvestmentEvent!.AffectedAssets.Any(a => ids.Contains(a.Id))).ToListAsync(ct);
        assessments = (await current.EnsureFreshAsync(portfolio, targets, assessments, ct)).ToList();
        var inputs = assessments.Where(x => x.InvestmentEvent is not null).SelectMany(x => x.InvestmentEvent!.AffectedAssets.Where(a => ids.Contains(a.Id)).Select(a => new GuidanceAssessment(a.Id, x.CapitalDecisionImpact, x.Status, x.InvestmentEvent.Evidence.FirstOrDefault()?.Reference, x.AssessmentVersion))).ToList();
        var fallback = new ContributionGuidanceCalculator().Calculate(new ContributionGuidanceInput(portfolioId, amount, health, positions, inputs));
        var persisted = new PersistedContributionGuidance(portfolioId, amount, snapshot, targetKey, "R4-2.0", "pending-worker", "pending", "PartialIntelligence", "A orientação será enriquecida pelo Worker.", JsonSerializer.Serialize(fallback.Reasons));
        foreach (var allocation in fallback.Allocations) persisted.Allocations.Add(new PersistedContributionGuidanceAllocation(persisted.Id, allocation.AssetId, allocation.Amount, allocation.Reason, 0, JsonSerializer.Serialize(allocation.Evidence)));
        db.ContributionGuidances.Add(persisted); var job = new ContributionGuidanceJob(portfolioId, persisted.Id, JsonSerializer.Serialize(positions)); db.ContributionGuidanceJobs.Add(job); await db.SaveChangesAsync(ct);
        return Accepted(new { guidanceId = persisted.Id, status = fallback.Status.ToString(), contributionAmount = fallback.ContributionAmount, requestedAmount = fallback.RequestedAmount, allocatedAmount = fallback.AllocatedAmount, unallocatedAmount = fallback.UnallocatedAmount, allocationStyle = fallback.AllocationStyle.ToString(), allocations = fallback.Allocations, avoidedCandidates = fallback.AvoidedCandidates, reasons = fallback.Reasons, evidence = fallback.Evidence, intelligenceAvailability = persisted.IntelligenceAvailability, jobId = job.Id, jobStatus = job.Status.ToString(), reused = false });
    }

    [HttpGet("{guidanceId:guid}")]
    public async Task<ActionResult<object>> Get(Guid portfolioId, Guid guidanceId, CancellationToken ct)
    {
        var item = await db.ContributionGuidances.AsNoTracking().Include(x => x.Allocations).SingleOrDefaultAsync(x => x.Id == guidanceId && x.PortfolioId == portfolioId, ct); if (item is null) return NotFound();
        var positions = await db.PortfolioPositions.AsNoTracking().Include(x => x.Asset).Where(x => x.PortfolioId == portfolioId).ToDictionaryAsync(x => x.AssetId, ct);
        var allocations = item.Allocations.Select(x => new { x.AssetId, ticker = positions.GetValueOrDefault(x.AssetId)?.Asset?.Ticker ?? "", assetClass = (int)(positions.GetValueOrDefault(x.AssetId)?.Asset?.AssetClass ?? AssetClass.Other), x.Amount, x.Reason, evidence = Array.Empty<string>() }).ToArray();
        var status = await db.ContributionGuidanceJobs.Where(x => x.ContributionGuidanceId == guidanceId).Select(x => x.Status.ToString()).FirstOrDefaultAsync(ct);
        return Ok(new { guidanceId = item.Id, status = "Calculated", contributionAmount = item.RequestedAmount, requestedAmount = item.RequestedAmount, allocatedAmount = allocations.Sum(x => x.Amount), unallocatedAmount = item.RequestedAmount - allocations.Sum(x => x.Amount), allocationStyle = allocations.Length == 1 ? "Concentrated" : allocations.Length > 1 ? "Distributed" : "NoAllocation", allocations, avoidedCandidates = Array.Empty<object>(), reasons = new[] { item.Reasoning }, evidence = Array.Empty<string>(), item.IntelligenceAvailability, item.Reasoning, jobStatus = status, reused = true });
    }
}
public sealed record ContributionRequest(decimal Amount);
