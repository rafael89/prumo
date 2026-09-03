using Microsoft.AspNetCore.Mvc;
using Prumo.Infrastructure.Performance;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/performance")]
public sealed class PortfolioPerformanceController(PortfolioPerformanceService performance) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<object>> Get(Guid portfolioId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (from.HasValue && to.HasValue && from > to) return BadRequest("O início do período deve ser anterior ao fim.");
        var result = await performance.GetAsync(portfolioId, from, to, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
