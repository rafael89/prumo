using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lodira.Application.Contributions;
using Lodira.Application.Portfolio;
using Lodira.Application.Abstractions;
using Lodira.Application.Intelligence;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Infrastructure.Persistence;
using Lodira.Api.Services;

namespace Lodira.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}")]
public sealed class ContributionGuidanceController : ControllerBase
{
    private readonly LodiraDbContext db;
    private readonly IPortfolioAssessmentFreshnessEvaluator freshnessEvaluator = new PortfolioAssessmentFreshnessEvaluator();
    private readonly PortfolioAssessmentCurrentService currentAssessmentService;
    public ContributionGuidanceController(LodiraDbContext db, PortfolioAssessmentCurrentService? currentAssessmentService = null) { this.db = db; var evaluator = new PortfolioAssessmentFreshnessEvaluator(); this.currentAssessmentService = currentAssessmentService ?? new PortfolioAssessmentCurrentService(db, new PortfolioImpactAssessmentService(), evaluator, Microsoft.Extensions.Logging.Abstractions.NullLogger<PortfolioAssessmentCurrentService>.Instance); }

    [HttpGet("allocation-strategy")]
    public async Task<ActionResult<AllocationStrategyResponse>> GetStrategy(Guid portfolioId, CancellationToken ct)
    {
        if (!await db.Portfolios.AnyAsync(p => p.Id == portfolioId, ct)) return NotFound();
        var strategy = await db.PortfolioAllocationStrategies.AsNoTracking().Include(s => s.Allocations).SingleOrDefaultAsync(s => s.PortfolioId == portfolioId && s.ArchivedAt == null, ct);
        return strategy is null ? NotFound() : Ok(AllocationStrategyResponse.From(strategy));
    }

    [HttpPut("allocation-strategy")]
    public async Task<ActionResult<AllocationStrategyResponse>> PutStrategy(Guid portfolioId, AllocationStrategyRequest request, CancellationToken ct)
    {
        if (!await db.Portfolios.AnyAsync(p => p.Id == portfolioId, ct)) return NotFound();
        if (request.Allocations is null || request.Allocations.Count == 0) return BadRequest("Defina ao menos uma classe de ativos.");
        if (request.Allocations.Any(t => t.TargetPercentage <= 0 || t.TargetPercentage > 100) || request.Allocations.Any(t => !Enum.IsDefined(t.AssetClass))) return BadRequest("Defina classes e percentuais válidos.");
        if (request.Allocations.GroupBy(t => t.AssetClass).Any(g => g.Count() > 1) || Math.Abs(request.Allocations.Sum(t => t.TargetPercentage) - 100m) > .01m) return BadRequest("A soma dos alvos deve ser 100% e as classes não podem se repetir.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var current = await db.PortfolioAllocationStrategies.SingleOrDefaultAsync(s => s.PortfolioId == portfolioId && s.ArchivedAt == null, ct);
        var version = (await db.PortfolioAllocationStrategies.Where(s => s.PortfolioId == portfolioId).MaxAsync(s => (int?)s.Version, ct) ?? 0) + 1; current?.Archive();
        var strategy = new PortfolioAllocationStrategy(portfolioId, request.Name ?? "Minha estratégia", request.TemplateKey, version, request.IncomePriority, request.GrowthPriority, request.CapitalPreservationPriority, request.ContributionPolicy, request.MaximumAssetWeight);
        db.PortfolioAllocationStrategies.Add(strategy); foreach (var allocation in request.Allocations) db.StrategyAllocations.Add(new StrategyAllocation(strategy.Id, allocation.AssetClass, allocation.TargetPercentage));
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return CreatedAtAction(nameof(GetStrategy), new { portfolioId }, AllocationStrategyResponse.From(strategy));
    }

    [HttpPost("contribution-guidance")]
    public async Task<ActionResult<ContributionGuidance>> Simulate(Guid portfolioId, ContributionRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0 || request.Amount > 1_000_000_000m) return BadRequest("O valor do aporte deve ser positivo e razoável.");
        var portfolio = await db.Portfolios.Include(p => p.Positions).ThenInclude(p => p.Asset).SingleOrDefaultAsync(p => p.Id == portfolioId, ct);
        if (portfolio is null) return NotFound();
        var strategy = await db.PortfolioAllocationStrategies.Include(s => s.Allocations).SingleOrDefaultAsync(s => s.PortfolioId == portfolioId && s.ArchivedAt == null, ct);
        if (strategy is null) return Conflict(new { status = ContributionGuidanceStatus.NotConfigured.ToString(), message = "Defina uma estratégia de alocação antes de simular um aporte." });
        var objective = await db.InvestmentObjectives.AsNoTracking().SingleOrDefaultAsync(o => o.PortfolioId == portfolioId && o.ArchivedAt == null, ct);
        if (portfolio.Positions.Any(p => !p.CurrentValue.HasValue)) return BadRequest("Não há preço válido para todas as posições existentes.");

        var originalWeights = portfolio.Positions.ToDictionary(p => p.Id, p => p.PortfolioWeight);
        var health = new PortfolioHealthCalculator().Calculate(portfolio, strategy);
        // PortfolioHealth reusa a matemática R2; restaura a projeção mutável após o cálculo.
        foreach (var position in portfolio.Positions) position.SetPortfolioWeight(originalWeights[position.Id]);
        var total = portfolio.Positions.Sum(p => p.CurrentValue ?? 0);
        var positions = portfolio.Positions.Where(p => p.Asset is not null && p.CurrentValue.HasValue).Select(p => new GuidancePosition(p.AssetId, p.Asset!.Ticker, p.Asset.AssetClass, p.CurrentValue!.Value, total == 0 ? 0 : p.CurrentValue.Value / total * 100m)).ToList();
        var assetIds = positions.Select(p => p.AssetId).ToHashSet();
        var assessments = await db.PortfolioAssessments.AsNoTracking().Include(a => a.InvestmentEvent!).ThenInclude(e => e.AffectedAssets).Include(a => a.InvestmentEvent!).ThenInclude(e => e.Evidence).Where(a => a.PortfolioId == portfolioId && a.Status != AssessmentStatus.Closed && a.InvestmentEvent!.AffectedAssets.Any(asset => assetIds.Contains(asset.Id))).ToListAsync(ct);
        assessments = (await currentAssessmentService.EnsureFreshAsync(portfolio, strategy, objective, assessments, ct)).ToList();
        var assessmentInputs = assessments.SelectMany(a => a.InvestmentEvent!.AffectedAssets.Where(asset => assetIds.Contains(asset.Id)).Select(asset => new GuidanceAssessment(asset.Id, a.CapitalDecisionImpact, a.Status, a.InvestmentEvent.Evidence.FirstOrDefault()?.Reference, a.AssessmentVersion))).ToList();
        return Ok(new ContributionGuidanceCalculator().Calculate(new ContributionGuidanceInput(portfolioId, request.Amount, strategy, health, positions, assessmentInputs)));
    }
}

public sealed record AllocationStrategyRequest(IReadOnlyList<StrategyAllocationRequest> Allocations, string? Name = null, string? TemplateKey = null, PriorityLevel IncomePriority = PriorityLevel.Medium, PriorityLevel GrowthPriority = PriorityLevel.Medium, PriorityLevel CapitalPreservationPriority = PriorityLevel.Medium, ContributionPolicy ContributionPolicy = ContributionPolicy.CorrectImbalancesFirst, decimal? MaximumAssetWeight = null);
public sealed record StrategyAllocationRequest(AssetClass AssetClass, decimal TargetPercentage);
public sealed record StrategyAllocationResponse(AssetClass AssetClass, decimal TargetPercentage);
public sealed record AllocationStrategyResponse(Guid Id, IReadOnlyList<StrategyAllocationResponse> Allocations, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string Name, string? TemplateKey, int Version, PriorityLevel IncomePriority, PriorityLevel GrowthPriority, PriorityLevel CapitalPreservationPriority, ContributionPolicy ContributionPolicy, decimal? MaximumAssetWeight)
{
    public static AllocationStrategyResponse From(PortfolioAllocationStrategy x) => new(x.Id, x.Allocations.Select(a => new StrategyAllocationResponse(a.AssetClass, a.TargetPercentage)).ToList(), x.CreatedAt, x.UpdatedAt, x.Name, x.TemplateKey, x.Version, x.IncomePriority, x.GrowthPriority, x.CapitalPreservationPriority, x.ContributionPolicy, x.MaximumAssetWeight);
}
public sealed record ContributionRequest(decimal Amount);
