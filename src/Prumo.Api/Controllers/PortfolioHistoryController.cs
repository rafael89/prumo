using Microsoft.AspNetCore.Mvc;
using Prumo.Api.Services;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}")]
public sealed class PortfolioHistoryController(PortfolioHistoryService history) : ControllerBase
{
    [HttpGet("timeline")]
    public async Task<ActionResult<PortfolioTimelineResponse>> Timeline(Guid portfolioId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var response = await history.GetTimelineAsync(portfolioId, Math.Max(page, 1), Math.Clamp(pageSize, 1, 200), ct);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpGet("assets/{assetId:guid}/history")]
    public async Task<ActionResult<AssetHistoryResponse>> AssetHistory(Guid portfolioId, Guid assetId, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var response = await history.GetAssetHistoryAsync(portfolioId, assetId, Math.Clamp(limit, 1, 200), ct);
        return response is null ? NotFound() : Ok(response);
    }
}
