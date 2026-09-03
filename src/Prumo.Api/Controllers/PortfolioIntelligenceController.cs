using Microsoft.AspNetCore.Mvc;
using Prumo.Infrastructure.Intelligence;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/intelligence")]
public sealed class PortfolioIntelligenceController(PortfolioIntelligenceService intelligence) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<object>> Get(Guid portfolioId, CancellationToken ct)
    {
        var result = await intelligence.GetAsync(portfolioId, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
