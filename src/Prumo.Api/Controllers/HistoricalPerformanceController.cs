using Microsoft.AspNetCore.Mvc;
using Prumo.Infrastructure.Performance;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/performance/history")]
public sealed class HistoricalPerformanceController(HistoricalPerformanceService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<HistoricalPerformanceDto>> Get(Guid portfolioId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        if (from > to) return BadRequest("O início do período deve ser anterior ao fim.");
        var result = await service.Get(portfolioId, from, to, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
