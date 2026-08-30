using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Domain.Rules;
using Lodira.Infrastructure.Persistence;

namespace Lodira.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/contributions")]
public sealed class PortfolioTransactionsController(LodiraDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<object>> Confirm(Guid portfolioId, ConfirmContributionRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0) return BadRequest("Informe ao menos um ativo comprado.");
        if (request.Items.Any(x => x.Quantity <= 0 || x.UnitPrice <= 0)) return BadRequest("Quantidade e preço devem ser positivos.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); var portfolio = await db.Portfolios.Include(x => x.Positions).ThenInclude(x => x.Asset).SingleOrDefaultAsync(x => x.Id == portfolioId, ct);
        if (portfolio is null) return NotFound();
        var guidance = request.GuidanceId.HasValue ? await db.ContributionGuidances.SingleOrDefaultAsync(x => x.Id == request.GuidanceId && x.PortfolioId == portfolioId, ct) : null;
        if (request.GuidanceId.HasValue && guidance is null) return NotFound("GuidanceNotFound");
        if (request.Items.Select(x => x.AssetId).Distinct().Count() != request.Items.Count || request.Items.Any(x => portfolio.Positions.All(p => p.AssetId != x.AssetId))) return BadRequest("Só é permitido registrar compras de ativos já existentes na carteira.");
        var total = request.Items.Sum(x => x.Quantity * x.UnitPrice); if (guidance is not null && total > guidance.RequestedAmount) return BadRequest("O total confirmado não pode exceder o valor da guidance.");
        var transaction = new PortfolioTransaction(portfolioId, PortfolioTransactionType.Buy, request.OccurredAt ?? DateTimeOffset.UtcNow, request.GuidanceId, total);
        foreach (var item in request.Items) { transaction.Items.Add(new PortfolioTransactionItem(transaction.Id, item.AssetId, item.Quantity, item.UnitPrice)); portfolio.Positions.Single(x => x.AssetId == item.AssetId).ApplyBuy(item.Quantity, item.UnitPrice); }
        PortfolioCalculator.RecalculateWeights(portfolio.Positions); db.PortfolioTransactions.Add(transaction); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Ok(new { transactionId = transaction.Id, totalAmount = transaction.TotalAmount, portfolioSnapshotChanged = true });
    }
}

public sealed record ConfirmContributionRequest(Guid? GuidanceId, IReadOnlyList<ContributionPurchaseRequest> Items, DateTimeOffset? OccurredAt = null);
public sealed record ContributionPurchaseRequest(Guid AssetId, decimal Quantity, decimal UnitPrice);
