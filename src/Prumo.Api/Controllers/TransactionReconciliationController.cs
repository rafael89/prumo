using Microsoft.AspNetCore.Mvc;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Reconciliation;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/transaction-reconciliation")]
public sealed class TransactionReconciliationController(TransactionReconciliationService service) : ControllerBase
{
    [HttpGet("candidates")]
    public Task<IReadOnlyList<TransactionCandidateDto>> Candidates(Guid portfolioId, CancellationToken ct) => service.Candidates(portfolioId, ct);

    [HttpGet]
    public Task<IReadOnlyList<ReconciliationDto>> List(Guid portfolioId, CancellationToken ct) => service.List(portfolioId, ct);

    [HttpPost]
    public async Task<ActionResult<ReconciliationDto>> Resolve(Guid portfolioId, ReconciliationRequest request, CancellationToken ct)
    {
        try { return Ok(await service.Resolve(portfolioId, request, ct)); }
        catch (KeyNotFoundException e) { return NotFound(e.Message); }
        catch (ArgumentException e) { return BadRequest(e.Message); }
        catch (InvalidOperationException e) { return Conflict(e.Message); }
    }

    [HttpDelete("{reconciliationId:guid}")]
    public async Task<IActionResult> Delete(Guid portfolioId, Guid reconciliationId, CancellationToken ct) => await service.Delete(portfolioId, reconciliationId, ct) ? NoContent() : NotFound();
}
