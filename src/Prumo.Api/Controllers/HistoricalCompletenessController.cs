using Microsoft.AspNetCore.Mvc;
using Prumo.Infrastructure.Historical;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/historical-completeness")]
public sealed class HistoricalCompletenessController(HistoricalCompletenessService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PortfolioHistoricalCompletenessDto>> Get(Guid portfolioId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) => (await service.Get(portfolioId, from, to, ct)) is { } result ? Ok(result) : NotFound();
}
