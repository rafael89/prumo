using Microsoft.AspNetCore.Mvc;
using Prumo.Infrastructure.Historical;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/historical-state")]
public sealed class HistoricalStateController(HistoricalFinancialStateService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<HistoricalPortfolioStateDto>> Get(Guid portfolioId, [FromQuery] DateOnly asOf, [FromQuery] Guid? assetId, CancellationToken ct)
        => (await service.Get(portfolioId, asOf, assetId, ct)) is { } result ? Ok(result) : NotFound();
}
