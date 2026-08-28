using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lodira.Application.Contributions;
using Lodira.Application.Radar;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Domain.Rules;
using Lodira.Infrastructure.Persistence;

namespace Lodira.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}")]
public sealed class ContributionGuidanceController : ControllerBase
{
    private readonly LodiraDbContext db;
    public ContributionGuidanceController(LodiraDbContext db) => this.db = db;

    [HttpGet("allocation-strategy")]
    public async Task<ActionResult<AllocationStrategyResponse>> GetStrategy(Guid portfolioId, CancellationToken ct)
    {
        if (!await db.Portfolios.AnyAsync(p => p.Id == portfolioId, ct)) return NotFound();
        var strategy = await db.PortfolioAllocationStrategies.AsNoTracking().Include(s => s.Allocations).SingleOrDefaultAsync(s => s.PortfolioId == portfolioId, ct);
        return strategy is null ? NotFound() : Ok(new AllocationStrategyResponse(strategy.Id, strategy.Allocations.Select(t => new StrategyAllocationResponse(t.AssetClass, t.TargetPercentage)).ToList(), strategy.CreatedAt, strategy.UpdatedAt));
    }

    [HttpPut("allocation-strategy")]
    public async Task<ActionResult<AllocationStrategyResponse>> PutStrategy(Guid portfolioId, AllocationStrategyRequest request, CancellationToken ct)
    {
        if (!await db.Portfolios.AnyAsync(p => p.Id == portfolioId, ct)) return NotFound();
        if (request.Allocations is null || request.Allocations.Count == 0) return BadRequest("Defina ao menos uma classe de ativos.");
        if (request.Allocations.Any(t => t.TargetPercentage <= 0 || t.TargetPercentage > 100)) return BadRequest("Cada alvo deve estar entre 0 e 100%.");
        if (request.Allocations.GroupBy(t => t.AssetClass).Any(g => g.Count() > 1)) return BadRequest("Não é permitido repetir classes.");
        if (request.Allocations.Any(t => !Enum.IsDefined(t.AssetClass))) return BadRequest("Classe de ativo inválida.");
        if (Math.Abs(request.Allocations.Sum(t => t.TargetPercentage) - 100m) > .01m) return BadRequest("A soma dos alvos deve ser 100% (tolerância de 0,01 ponto percentual).");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var current = await db.PortfolioAllocationStrategies.SingleOrDefaultAsync(s => s.PortfolioId == portfolioId, ct);
        if (current is not null) db.PortfolioAllocationStrategies.Remove(current);
        var strategy = new PortfolioAllocationStrategy(portfolioId);
        db.PortfolioAllocationStrategies.Add(strategy);
        foreach (var allocation in request.Allocations) db.StrategyAllocations.Add(new StrategyAllocation(strategy.Id, allocation.AssetClass, allocation.TargetPercentage));
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return CreatedAtAction(nameof(GetStrategy), new { portfolioId }, new AllocationStrategyResponse(strategy.Id, request.Allocations.Select(t => new StrategyAllocationResponse(t.AssetClass, t.TargetPercentage)).ToList(), strategy.CreatedAt, strategy.UpdatedAt));
    }

    [HttpPost("contribution-guidance")]
    public async Task<ActionResult<ContributionGuidanceResult>> Simulate(Guid portfolioId, ContributionRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0 || request.Amount > 1_000_000_000m) return BadRequest("O valor do aporte deve ser positivo e razoável.");
        var portfolio = await db.Portfolios.Include(p => p.Positions).ThenInclude(p => p.Asset).SingleOrDefaultAsync(p => p.Id == portfolioId, ct);
        if (portfolio is null) return NotFound();
        if (portfolio.Positions.Any(p => !p.CurrentValue.HasValue)) return BadRequest("Não há preço válido para: " + string.Join(", ", portfolio.Positions.Where(p => !p.CurrentValue.HasValue).Select(p => p.Asset?.Ticker)));
        var strategy = await db.PortfolioAllocationStrategies.Include(s => s.Allocations).SingleOrDefaultAsync(s => s.PortfolioId == portfolioId, ct);
        if (strategy is null) return Conflict("Defina uma estratégia de alocação antes de simular um aporte.");
        var analyses = await db.DocumentAnalyses.AsNoTracking().Include(a => a.Document).OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).ToListAsync(ct);
        var latest = analyses.GroupBy(a => a.DocumentId).Select(g => g.First()).ToList();
        var states = await db.RadarItemStates.AsNoTracking().ToDictionaryAsync(s => s.DocumentAnalysisId, ct);
        var calculator = new RadarPriorityCalculator();
        var blocked = latest.Where(a => states.TryGetValue(a.Id, out var state) && RadarAttentionPolicy.RequiresAttention(calculator.Calculate(a).Priority, a.RequiresAction, state.Status) && a.AffectedAssets.Count > 0)
            .SelectMany(a => a.AffectedAssets).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var derived = new EqualWeightAssetTargetDerivationPolicy().Derive(
            portfolio.Positions.Where(p => p.Quantity > 0 && p.Asset is not null).Select(p => new AssetForTarget(p.AssetId, p.Asset!.Ticker, p.Asset.AssetClass, p.CurrentValue!.Value)),
            strategy.Allocations.Select(a => (a.AssetClass, a.TargetPercentage)));
        var positions = derived.Select(t => new ContributionPosition(t.AssetId, t.Ticker, t.CurrentValue, t.TargetPercentage, blocked.Contains(t.Ticker)));
        return Ok(new ContributionGuidanceCalculator().Calculate(positions, request.Amount));
    }
}

public sealed record AllocationStrategyRequest(IReadOnlyList<StrategyAllocationRequest> Allocations);
public sealed record StrategyAllocationRequest(AssetClass AssetClass, decimal TargetPercentage);
public sealed record StrategyAllocationResponse(AssetClass AssetClass, decimal TargetPercentage);
public sealed record AllocationStrategyResponse(Guid Id, IReadOnlyList<StrategyAllocationResponse> Allocations, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record ContributionRequest(decimal Amount);
