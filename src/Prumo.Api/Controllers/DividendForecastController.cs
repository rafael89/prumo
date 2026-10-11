using Microsoft.AspNetCore.Mvc;
using Prumo.Api.Services;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/dividend-forecast")]
public sealed class DividendForecastController(DividendForecastService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DividendForecastResponse>> Get(Guid portfolioId, CancellationToken ct, [FromQuery] DateOnly? month = null)
    {
        var response = await service.GetAsync(portfolioId, ct, month);
        return response is null ? NotFound() : Ok(response);
    }
}
