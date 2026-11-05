using Microsoft.AspNetCore.Mvc;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Historical;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}")]
public sealed class FinancialHistoryController(HistoricalAnchorService anchors, CoverageService coverage, HistoricalCashStateService cash) : ControllerBase
{
    [HttpGet("cash-anchors")]
    public Task<IReadOnlyList<CashAnchorDto>> CashAnchors(Guid portfolioId, CancellationToken ct) => cash.ListAnchors(portfolioId, ct);

    [HttpPost("cash-anchors")]
    public async Task<ActionResult<CashAnchorDto>> CreateCashAnchor(Guid portfolioId, CashAnchorRequest request, CancellationToken ct) => await Execute(() => cash.CreateAnchor(portfolioId, request, ct));

    [HttpPut("cash-anchors/{anchorId:guid}")]
    public async Task<ActionResult<CashAnchorDto>> UpdateCashAnchor(Guid portfolioId, Guid anchorId, CashAnchorRequest request, CancellationToken ct) => await Execute(async () => (await cash.UpdateAnchor(portfolioId, anchorId, request, ct)) is { } result ? result : throw new KeyNotFoundException("Âncora de caixa não encontrada."));

    [HttpDelete("cash-anchors/{anchorId:guid}")]
    public async Task<IActionResult> DeleteCashAnchor(Guid portfolioId, Guid anchorId, CancellationToken ct) => await cash.DeleteAnchor(portfolioId, anchorId, ct) ? NoContent() : NotFound();

    [HttpGet("cash-state")]
    public async Task<ActionResult<HistoricalCashStateDto>> CashState(Guid portfolioId, [FromQuery] DateOnly asOf, CancellationToken ct) => (await cash.Get(portfolioId, asOf, ct)) is { } result ? Ok(result) : NotFound();
    [HttpGet("historical-anchors")]
    public Task<IReadOnlyList<HistoricalAnchorDto>> Anchors(Guid portfolioId, CancellationToken ct) => anchors.List(portfolioId, ct);

    [HttpGet("historical-anchors/{anchorId:guid}")]
    public async Task<ActionResult<HistoricalAnchorDto>> Anchor(Guid portfolioId, Guid anchorId, CancellationToken ct) => (await anchors.Get(portfolioId, anchorId, ct)) is { } result ? Ok(result) : NotFound();

    [HttpGet("historical-anchors/{anchorId:guid}/validation")]
    public async Task<ActionResult<AnchorValidationDto>> ValidateAnchor(Guid portfolioId, Guid anchorId, CancellationToken ct) => (await anchors.Validate(portfolioId, anchorId, coverage, ct)) is { } result ? Ok(result) : NotFound();

    [HttpPost("historical-anchors")]
    public async Task<ActionResult<HistoricalAnchorDto>> CreateAnchor(Guid portfolioId, HistoricalAnchorRequest request, CancellationToken ct) => await Execute(() => anchors.Create(portfolioId, request, ct));

    [HttpPut("historical-anchors/{anchorId:guid}")]
    public async Task<ActionResult<HistoricalAnchorDto>> UpdateAnchor(Guid portfolioId, Guid anchorId, HistoricalAnchorRequest request, CancellationToken ct)
    {
        return await Execute(async () => (await anchors.Update(portfolioId, anchorId, request, ct))
            ?? throw new KeyNotFoundException("Âncora histórica não encontrada."));
    }

    [HttpDelete("historical-anchors/{anchorId:guid}")]
    public async Task<IActionResult> DeleteAnchor(Guid portfolioId, Guid anchorId, CancellationToken ct) => await anchors.Delete(portfolioId, anchorId, ct) ? NoContent() : NotFound();

    [HttpGet("financial-history-coverage")]
    public Task<IReadOnlyList<CoverageEvidenceDto>> Coverage(Guid portfolioId, [FromQuery] FinancialHistoryCoverageDimension? dimension, [FromQuery] Guid? assetId, CancellationToken ct) => coverage.List(portfolioId, ct, dimension, assetId);

    [HttpGet("financial-history-coverage/status")]
    public Task<CoverageStatusDto> CoverageStatus(Guid portfolioId, [FromQuery] FinancialHistoryCoverageDimension dimension, [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? assetId, CancellationToken ct) => coverage.Resolve(portfolioId, dimension, from, to, assetId, ct);

    [HttpPost("financial-history-coverage")]
    public async Task<ActionResult<CoverageEvidenceDto>> CreateCoverage(Guid portfolioId, CoverageRequest request, CancellationToken ct) => await Execute(() => coverage.Create(portfolioId, request, ct));

    [HttpDelete("financial-history-coverage/{coverageId:guid}")]
    public async Task<IActionResult> DeleteCoverage(Guid portfolioId, Guid coverageId, CancellationToken ct) => await coverage.Delete(portfolioId, coverageId, ct) ? NoContent() : NotFound();

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException e) { return NotFound(e.Message); }
        catch (ArgumentException e) { return BadRequest(e.Message); }
        catch (InvalidOperationException e) { return Conflict(e.Message); }
    }
}
