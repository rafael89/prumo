using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/target-allocations")]
public sealed class TargetAllocationsController(PrumoDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TargetAllocationResponse>>> Get(Guid portfolioId, CancellationToken ct)
    {
        if (!await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct)) return NotFound();
        return Ok(await db.TargetAllocations.AsNoTracking().Where(x => x.PortfolioId == portfolioId).OrderBy(x => x.AssetClass).Select(x => new TargetAllocationResponse(x.AssetClass, x.TargetPercentage)).ToListAsync(ct));
    }

    [HttpPut]
    public async Task<ActionResult> Replace(Guid portfolioId, ReplaceTargetAllocationsRequest request, CancellationToken ct)
    {
        if (request.Allocations is null || request.Allocations.Count == 0) return BadRequest("Informe as alocações da carteira.");
        if (request.Allocations.Select(x => x.AssetClass).Distinct().Count() != request.Allocations.Count || request.Allocations.Any(x => !Enum.IsDefined(x.AssetClass) || x.TargetPercentage < 0 || x.TargetPercentage > 100)) return BadRequest("As classes devem ser únicas e os percentuais estar entre 0 e 100.");
        if (Math.Abs(request.Allocations.Sum(x => x.TargetPercentage) - 100m) > .01m) return BadRequest("A soma das alocações deve ser 100%.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsRelational())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Portfolios\" WHERE \"Id\" = {portfolioId} FOR UPDATE", ct);
        if (!await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct)) return NotFound();
        var existing = await db.TargetAllocations.Where(x => x.PortfolioId == portfolioId).ToListAsync(ct);
        db.TargetAllocations.RemoveRange(existing);
        db.TargetAllocations.AddRange(request.Allocations.Select(x => new TargetAllocation(portfolioId, x.AssetClass, x.TargetPercentage)));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }
}

public sealed record ReplaceTargetAllocationsRequest(IReadOnlyList<TargetAllocationRequest> Allocations);
public sealed record TargetAllocationRequest(AssetClass AssetClass, decimal TargetPercentage);
public sealed record TargetAllocationResponse(AssetClass AssetClass, decimal TargetPercentage);
