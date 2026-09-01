using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lodira.Application.Contributions;
using Lodira.Application.Explainability;
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
    private const string ContributionEngineVersion = "R4-3.0";

    [HttpPost]
    public async Task<ActionResult<object>> Generate(Guid portfolioId, ContributionRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0) return BadRequest("O valor do aporte deve ser positivo.");

        var portfolio = await db.Portfolios.Include(x => x.Positions).ThenInclude(x => x.Asset)
            .SingleOrDefaultAsync(x => x.Id == portfolioId, ct);
        if (portfolio is null) return NotFound();

        var targets = await db.TargetAllocations.AsNoTracking().Where(x => x.PortfolioId == portfolioId).ToListAsync(ct);
        if (targets.Count == 0) return Conflict(new { status = ContributionGuidanceStatus.NotConfigured.ToString() });
        if (portfolio.Positions.Any(x => !x.CurrentValue.HasValue)) return BadRequest("Não há preço válido para todas as posições existentes.");

        var snapshot = PortfolioSnapshotKeyCalculator.Calculate(portfolio);
        var targetKey = TargetAllocationKeyCalculator.Calculate(targets)!;
        var amount = decimal.Round(request.Amount, 2);

        var health = new PortfolioHealthCalculator().Calculate(portfolio, targets);
        var total = portfolio.Positions.Sum(x => x.CurrentValue ?? 0);
        var positions = portfolio.Positions.Where(x => x.Asset is not null && x.CurrentValue.HasValue)
            .Select(x => new GuidancePosition(x.AssetId, x.Asset!.Ticker, x.Asset.AssetClass, x.CurrentValue!.Value, total == 0 ? 0 : x.CurrentValue.Value / total * 100m)).ToList();
        var ids = positions.Select(x => x.AssetId).ToHashSet();
        var assessments = await db.PortfolioAssessments.AsNoTracking()
            .Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets)
            .Include(x => x.InvestmentEvent!).ThenInclude(x => x.Evidence)
            .Where(x => x.PortfolioId == portfolioId && x.Status != AssessmentStatus.Closed && x.InvestmentEvent!.AffectedAssets.Any(a => ids.Contains(a.Id))).ToListAsync(ct);
        assessments = (await current.EnsureFreshAsync(portfolio, targets, assessments, ct)).ToList();

        var inputs = assessments.Where(x => x.InvestmentEvent is not null)
            .SelectMany(x => x.InvestmentEvent!.AffectedAssets.Where(a => ids.Contains(a.Id)).Select(a => new GuidanceAssessment(
                a.Id, x.CapitalDecisionImpact, x.Status, x.InvestmentEvent.Evidence.FirstOrDefault()?.Reference, x.AssessmentVersion,
                x.AttentionPriority, x.ActionRequirement, x.Summary, x.WhyItMatters, x.SuggestedBehavior, ConfidenceValue(x.Confidence), x.Id))).ToList();
        var intelligenceContextKey = ContributionIntelligenceContextKeyCalculator.Calculate(inputs);
        var existing = await db.ContributionGuidances.AsNoTracking().Include(x => x.Allocations)
            .Where(x => x.PortfolioId == portfolioId
                && x.RequestedAmount == amount
                && x.PortfolioSnapshotKey == snapshot
                && x.TargetAllocationKey == targetKey
                && x.IntelligenceContextKey == intelligenceContextKey
                && x.EngineVersion == ContributionEngineVersion)
            .OrderByDescending(x => x.GeneratedAt).FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            var existingStatus = await db.ContributionGuidanceJobs.AsNoTracking()
                .Where(x => x.ContributionGuidanceId == existing.Id).OrderByDescending(x => x.CreatedAt)
                .Select(x => (ContributionGuidanceJobStatus?)x.Status).FirstOrDefaultAsync(ct);
            if (existingStatus is ContributionGuidanceJobStatus.Completed)
                return Ok(await CompletedResponseAsync(portfolioId, existing, ct));
            if (existingStatus is ContributionGuidanceJobStatus.Queued or ContributionGuidanceJobStatus.Processing)
                return Accepted(PendingResponse(existing.Id, existing.RequestedAmount, existingStatus.Value, true));
        }

        var baseline = new ContributionGuidanceCalculator().Calculate(new ContributionGuidanceInput(portfolioId, amount, health, positions, inputs));
        var classBudgets = baseline.Allocations.GroupBy(x => x.AssetClass).ToDictionary(x => x.Key, x => x.Sum(a => a.Amount));
        var deviations = health.AllocationDeviations.GroupBy(x => x.AssetClass).ToDictionary(x => x.Key, x => x.First());
        var candidates = positions.Select(position =>
        {
            var contexts = inputs.Where(x => x.AssetId == position.AssetId).Select(x => new ContributionAssessmentContext(
                x.AttentionPriority.ToString(), x.ActionRequirement.ToString(), x.Impact.ToString(), x.Summary, x.WhyItMatters,
                x.SuggestedBehavior, x.Confidence,
                new[] { new EvidenceReference("assessment", $"{x.AssessmentId}:{x.AssessmentVersion}", x.Evidence ?? x.Summary, x.WhyItMatters) })).ToArray();
            var deviation = deviations.GetValueOrDefault(position.AssetClass);
            return new ContributionCandidate(position.AssetId, position.Ticker, position.AssetClass, position.CurrentValue, position.CurrentWeight,
                deviation?.CurrentPercentage ?? 0, deviation?.TargetPercentage ?? 0, classBudgets.GetValueOrDefault(position.AssetClass),
                inputs.Any(x => x.AssetId == position.AssetId && x.Impact == CapitalDecisionImpact.Block), contexts);
        }).ToArray();

        var persisted = new PersistedContributionGuidance(portfolioId, amount, snapshot, targetKey, intelligenceContextKey, ContributionEngineVersion, "pending-ai", "pending", "Pending", "A IA está analisando os candidatos e os limites da carteira.", "{}");
        db.ContributionGuidances.Add(persisted);
        var job = new ContributionGuidanceJob(portfolioId, persisted.Id, JsonSerializer.Serialize(new ContributionGuidanceWorkItem(baseline, positions, candidates)));
        db.ContributionGuidanceJobs.Add(job);
        await db.SaveChangesAsync(ct);

        return Accepted(PendingResponse(persisted.Id, amount, job.Status, false));
    }

    [HttpGet("{guidanceId:guid}")]
    public async Task<ActionResult<object>> Get(Guid portfolioId, Guid guidanceId, CancellationToken ct)
    {
        var item = await db.ContributionGuidances.AsNoTracking().Include(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.Id == guidanceId && x.PortfolioId == portfolioId, ct);
        if (item is null) return NotFound();

        var status = await db.ContributionGuidanceJobs.AsNoTracking().Where(x => x.ContributionGuidanceId == guidanceId)
            .OrderByDescending(x => x.CreatedAt).Select(x => (ContributionGuidanceJobStatus?)x.Status).FirstOrDefaultAsync(ct);
        if (status is not ContributionGuidanceJobStatus.Completed)
            return Ok(PendingResponse(item.Id, item.RequestedAmount, status ?? ContributionGuidanceJobStatus.Failed, true));

        return Ok(await CompletedResponseAsync(portfolioId, item, ct));
    }

    private async Task<object> CompletedResponseAsync(Guid portfolioId, PersistedContributionGuidance item, CancellationToken ct)
    {
        var positions = await db.PortfolioPositions.AsNoTracking().Include(x => x.Asset)
            .Where(x => x.PortfolioId == portfolioId).ToDictionaryAsync(x => x.AssetId, ct);
        var allocations = item.Allocations.Select(x => new
        {
            x.AssetId,
            ticker = positions.GetValueOrDefault(x.AssetId)?.Asset?.Ticker ?? string.Empty,
            assetClass = (int)(positions.GetValueOrDefault(x.AssetId)?.Asset?.AssetClass ?? AssetClass.Other),
            x.Amount,
            x.Reason,
            evidence = DeserializeEvidence(x.EvidenceJson)
        }).ToArray();
        var allocated = allocations.Sum(x => x.Amount);
        return new
        {
            guidanceId = item.Id,
            status = allocations.Length == 0 ? ContributionGuidanceStatus.NoEligibleCandidates.ToString() : ContributionGuidanceStatus.Calculated.ToString(),
            contributionAmount = item.RequestedAmount,
            requestedAmount = item.RequestedAmount,
            allocatedAmount = allocated,
            unallocatedAmount = item.RequestedAmount - allocated,
            allocationStyle = allocations.Length == 1 ? "Concentrated" : allocations.Length > 1 ? "Distributed" : "NoAllocation",
            allocations,
            avoidedCandidates = Array.Empty<object>(),
            reasons = string.IsNullOrWhiteSpace(item.Reasoning) ? Array.Empty<string>() : new[] { item.Reasoning },
            evidence = allocations.SelectMany(x => x.evidence).Distinct().ToArray(),
            item.IntelligenceAvailability,
            item.Reasoning,
            jobStatus = ContributionGuidanceJobStatus.Completed.ToString(),
            reused = true
        };
    }

    private static object PendingResponse(Guid guidanceId, decimal amount, ContributionGuidanceJobStatus jobStatus, bool reused) => new
    {
        guidanceId,
        status = jobStatus == ContributionGuidanceJobStatus.Failed ? "Failed" : "Analyzing",
        contributionAmount = amount,
        requestedAmount = amount,
        allocatedAmount = 0m,
        unallocatedAmount = amount,
        allocationStyle = "Pending",
        allocations = Array.Empty<object>(),
        avoidedCandidates = Array.Empty<object>(),
        reasons = jobStatus == ContributionGuidanceJobStatus.Failed ? new[] { "Não foi possível concluir a análise. Tente gerar uma nova orientação." } : Array.Empty<string>(),
        evidence = Array.Empty<string>(),
        intelligenceAvailability = "Pending",
        jobStatus = jobStatus.ToString(),
        reused
    };

    private static decimal ConfidenceValue(IntelligenceConfidence confidence) => confidence switch
    {
        IntelligenceConfidence.High => 0.9m,
        IntelligenceConfidence.Medium => 0.7m,
        _ => 0.4m
    };

    private static string[] DeserializeEvidence(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
        catch (JsonException) { return Array.Empty<string>(); }
    }
}

public sealed record ContributionRequest(decimal Amount);
