using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Domain.Rules;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/contributions")]
public sealed class PortfolioTransactionsController(PrumoDbContext db) : ControllerBase
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
        var requestedAssetIds = request.Items.Select(x => x.AssetId).ToHashSet();
        var assets = await db.Assets.Where(x => requestedAssetIds.Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id, ct);
        if (assets.Count != requestedAssetIds.Count) return BadRequest("Só é permitido registrar compras de ativos conhecidos e ativos.");
        if (request.Items.Select(x => x.AssetId).Distinct().Count() != request.Items.Count) return BadRequest("Não repita o mesmo ativo no registro do aporte.");
        var total = request.Items.Sum(x => x.Quantity * x.UnitPrice); if (guidance is not null && total > guidance.RequestedAmount) return BadRequest("O total confirmado não pode exceder o valor da guidance.");
        var transaction = new PortfolioTransaction(portfolioId, PortfolioTransactionType.Buy, request.OccurredAt ?? DateTimeOffset.UtcNow, request.GuidanceId, total);
        foreach (var item in request.Items)
        {
            transaction.Items.Add(new PortfolioTransactionItem(transaction.Id, item.AssetId, item.Quantity, item.UnitPrice));
            var position = portfolio.Positions.SingleOrDefault(x => x.AssetId == item.AssetId);
            if (position is null)
            {
                position = new PortfolioPosition(portfolio.Id, item.AssetId, item.Quantity, item.UnitPrice, item.UnitPrice);
                portfolio.Positions.Add(position);
                db.PortfolioPositions.Add(position);
            }
            else position.ApplyBuy(item.Quantity, item.UnitPrice);
        }
        PortfolioCalculator.RecalculateWeights(portfolio.Positions); db.PortfolioTransactions.Add(transaction); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Ok(new { transactionId = transaction.Id, totalAmount = transaction.TotalAmount, portfolioSnapshotChanged = true });
    }
}

public sealed record ConfirmContributionRequest(Guid? GuidanceId, IReadOnlyList<ContributionPurchaseRequest> Items, DateTimeOffset? OccurredAt = null);
public sealed record ContributionPurchaseRequest(Guid AssetId, decimal Quantity, decimal UnitPrice);
