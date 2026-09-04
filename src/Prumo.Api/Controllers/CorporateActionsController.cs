using Microsoft.AspNetCore.Mvc;
using Prumo.Infrastructure.Historical;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/assets/{assetId:guid}/corporate-actions")]
public sealed class CorporateActionsController(CorporateActionService actions) : ControllerBase
{
    [HttpGet("~/api/portfolios/{portfolioId:guid}/corporate-actions/assets", Order = -1)]
    public Task<IReadOnlyList<HistoricalAssetDto>> HistoricalAssets(Guid portfolioId, CancellationToken ct) => actions.HistoricalAssets(portfolioId, ct);

    [HttpGet]
    public Task<IReadOnlyList<CorporateActionDto>> List(Guid assetId, CancellationToken ct) => actions.List(assetId, ct);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CorporateActionDto>> Get(Guid assetId, Guid id, CancellationToken ct) => (await actions.Get(assetId, id, ct)) is { } result ? Ok(result) : NotFound();

    [HttpPost]
    public async Task<ActionResult<CorporateActionDto>> Create(Guid assetId, CorporateActionRequest request, CancellationToken ct) => await Execute(() => actions.Create(assetId, request, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CorporateActionDto>> Update(Guid assetId, Guid id, CorporateActionRequest request, CancellationToken ct) => await Execute(async () => (await actions.Update(assetId, id, request, ct)) is { } result ? result : throw new KeyNotFoundException("Ação corporativa não encontrada."));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid assetId, Guid id, CancellationToken ct) => await actions.Delete(assetId, id, ct) ? NoContent() : NotFound();

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException e) { return NotFound(e.Message); }
        catch (ArgumentException e) { return BadRequest(e.Message); }
        catch (InvalidOperationException e) { return Conflict(e.Message); }
    }
}
