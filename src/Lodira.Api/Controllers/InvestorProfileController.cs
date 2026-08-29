using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Infrastructure.Persistence;

namespace Lodira.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}")]
public sealed class InvestorProfileController : ControllerBase
{
    private readonly LodiraDbContext db;
    public InvestorProfileController(LodiraDbContext db) => this.db = db;

    [HttpGet("objective")]
    public async Task<ActionResult<ObjectiveResponse>> GetObjective(Guid portfolioId, CancellationToken ct)
    {
        var item = await db.InvestmentObjectives.AsNoTracking().Where(x => x.PortfolioId == portfolioId && x.ArchivedAt == null).SingleOrDefaultAsync(ct);
        return item is null ? NotFound() : Ok(ObjectiveResponse.From(item));
    }

    [HttpPut("objective")]
    public async Task<ActionResult<ObjectiveResponse>> PutObjective(Guid portfolioId, ObjectiveRequest request, CancellationToken ct)
    {
        if (!await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct)) return NotFound();
        try
        {
            InvestmentObjective.ValidateTarget(request.TargetMetric, request.TargetAmount);
            var current = await db.InvestmentObjectives.Where(x => x.PortfolioId == portfolioId && x.ArchivedAt == null).SingleOrDefaultAsync(ct);
            var version = (await db.InvestmentObjectives.Where(x => x.PortfolioId == portfolioId).MaxAsync(x => (int?)x.Version, ct) ?? 0) + 1;
            current?.Archive();
            var objective = new InvestmentObjective(portfolioId, request.PrimaryGoal, request.TargetMetric, request.TargetAmount, request.TimeHorizon, version);
            db.InvestmentObjectives.Add(objective);
            await db.SaveChangesAsync(ct);
            return Ok(ObjectiveResponse.From(objective));
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet("strategy")]
    public async Task<ActionResult<StrategyResponse>> GetStrategy(Guid portfolioId, CancellationToken ct)
    {
        var item = await db.PortfolioAllocationStrategies.AsNoTracking().Include(x => x.Allocations).Where(x => x.PortfolioId == portfolioId && x.ArchivedAt == null).SingleOrDefaultAsync(ct);
        return item is null ? NotFound() : Ok(StrategyResponse.From(item));
    }
}

public sealed record ObjectiveRequest(InvestmentGoal PrimaryGoal, TargetMetric TargetMetric, decimal? TargetAmount, TimeHorizon TimeHorizon);
public sealed record ObjectiveResponse(Guid Id, InvestmentGoal PrimaryGoal, TargetMetric TargetMetric, decimal? TargetAmount, TimeHorizon TimeHorizon, int Version, DateTimeOffset CreatedAt)
{
    public static ObjectiveResponse From(InvestmentObjective x) => new(x.Id, x.PrimaryGoal, x.TargetMetric, x.TargetAmount, x.TimeHorizon, x.Version, x.CreatedAt);
}
public sealed record StrategyResponse(Guid Id, string Name, string? TemplateKey, int Version, PriorityLevel IncomePriority, PriorityLevel GrowthPriority, PriorityLevel CapitalPreservationPriority, ContributionPolicy ContributionPolicy, decimal? MaximumAssetWeight, IReadOnlyList<StrategyAllocationResponse> Allocations, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static StrategyResponse From(PortfolioAllocationStrategy x) => new(x.Id, x.Name, x.TemplateKey, x.Version, x.IncomePriority, x.GrowthPriority, x.CapitalPreservationPriority, x.ContributionPolicy, x.MaximumAssetWeight, x.Allocations.Select(a => new StrategyAllocationResponse(a.AssetClass, a.TargetPercentage)).ToList(), x.CreatedAt, x.UpdatedAt);
}
