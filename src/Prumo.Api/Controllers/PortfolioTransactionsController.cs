using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Domain.Rules;
using Prumo.Infrastructure.MarketData;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/contributions")]
public sealed class PortfolioTransactionsController(PrumoDbContext db, ExternalAssetMaterializer? materializer = null) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<object>> Confirm(Guid portfolioId, ConfirmContributionRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0) return BadRequest("Informe ao menos um ativo comprado.");
        if (request.Items.Any(x => x.Quantity <= 0 || x.UnitPrice <= 0)) return BadRequest("Quantidade e preço devem ser positivos.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsRelational())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Portfolios\" WHERE \"Id\" = {portfolioId} FOR UPDATE", ct);
        var portfolio = await db.Portfolios.Include(x => x.Positions).ThenInclude(x => x.Asset).SingleOrDefaultAsync(x => x.Id == portfolioId, ct);
        if (portfolio is null) return NotFound();
        var guidance = request.GuidanceId.HasValue ? await db.ContributionGuidances.Include(x => x.Allocations).SingleOrDefaultAsync(x => x.Id == request.GuidanceId && x.PortfolioId == portfolioId, ct) : null;
        if (request.GuidanceId.HasValue && guidance is null) return NotFound("GuidanceNotFound");
        var items = request.Items.ToList();
        if (guidance is not null && items.Any(x => x.AssetId == Guid.Empty && !string.IsNullOrWhiteSpace(x.CandidateKey)))
        {
            foreach (var external in items.Where(x => x.AssetId == Guid.Empty && x.CandidateKey is not null).ToArray())
            {
                var allocation = guidance.Allocations.SingleOrDefault(x => x.Origin == "ExternalDiscovery" && x.CandidateKey == external.CandidateKey);
                if (allocation is null || allocation.AssetClass is null || allocation.AssetType is null) return BadRequest("Candidato externo não pertence à guidance.");
                var id = await (materializer ?? new ExternalAssetMaterializer(db)).MaterializeAsync(new(allocation.Provider!, allocation.Ticker!, allocation.Name!, allocation.AssetType.Value, allocation.AssetClass.Value, allocation.Exchange, allocation.Currency, allocation.Sector, allocation.SourceReference), ct);
                items[items.IndexOf(external)] = external with { AssetId = id };
            }
        }
        var requestedAssetIds = items.Select(x => x.AssetId).ToHashSet();
        var assets = await db.Assets.Where(x => requestedAssetIds.Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id, ct);
        if (assets.Count != requestedAssetIds.Count) return BadRequest("Só é permitido registrar compras de ativos conhecidos e ativos.");
        if (items.Select(x => x.AssetId).Distinct().Count() != items.Count) return BadRequest("Não repita o mesmo ativo no registro do aporte.");
        var total = items.Sum(x => x.Quantity * x.UnitPrice);
        if (guidance is not null)
        {
            var previous = await db.PortfolioTransactions.AsNoTracking().Include(x => x.Items)
                .Where(x => x.ContributionGuidanceId == guidance.Id).ToListAsync(ct);
            if (previous.SelectMany(x => x.Items).Any(x => requestedAssetIds.Contains(x.AssetId)))
                return Conflict("GuidanceAlreadyConfirmed");
            if (previous.Sum(x => x.TotalAmount) + total > guidance.RequestedAmount)
                return BadRequest("O total confirmado não pode exceder o valor da guidance.");
        }
        var source = request.GuidanceId.HasValue ? FinancialSource.ContributionGuidanceExecution : FinancialSource.Manual;
        var transaction = new PortfolioTransaction(portfolioId, PortfolioTransactionType.Buy, request.OccurredAt ?? DateTimeOffset.UtcNow, request.GuidanceId, total, source: source);
        foreach (var item in items)
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
        PortfolioCalculator.RecalculateWeights(portfolio.Positions); db.PortfolioTransactions.Add(transaction);
        // Duplicate execution is checked under the portfolio lock above.
        // Unexpected persistence failures must reach the global handler and logs.
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(new { transactionId = transaction.Id, totalAmount = transaction.TotalAmount, portfolioSnapshotChanged = true });
    }
}

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/financial-ledger")]
public sealed class FinancialLedgerController(PrumoDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LedgerItemDto>>> Get(Guid portfolioId, CancellationToken ct)
    {
        if (!await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct)) return NotFound();
        var buysAndSells = await db.PortfolioTransactions.AsNoTracking().Include(x => x.Items).Where(x => x.PortfolioId == portfolioId).ToListAsync(ct);
        var flows = await db.PortfolioCashFlows.AsNoTracking().Include(x => x.Asset).Where(x => x.PortfolioId == portfolioId).ToListAsync(ct);
        var assets = await db.Assets.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var items = buysAndSells.SelectMany(t => t.Items.Select(i => new LedgerItemDto(t.OccurredAt, t.Type.ToString(), assets.GetValueOrDefault(i.AssetId)?.Ticker, i.Quantity, i.UnitPrice, i.Quantity * i.UnitPrice, t.Source.ToString(), t.DatePrecision.ToString())))
            .Concat(flows.Select(f => new LedgerItemDto(f.OccurredAt, f.Type.ToString(), f.Asset?.Ticker, null, null, f.Amount, f.Source.ToString(), f.DatePrecision.ToString())))
            .OrderByDescending(x => x.OccurredAt).ToList();
        return Ok(items);
    }

    [HttpPost("sells")]
    public async Task<ActionResult<object>> Sell(Guid portfolioId, SellRequest request, CancellationToken ct)
    {
        if (request.Quantity <= 0 || request.UnitPrice <= 0) return BadRequest("Quantidade e preço devem ser positivos.");
        if (!await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct) || !await db.Assets.AnyAsync(x => x.Id == request.AssetId && x.IsActive, ct)) return NotFound();
        var transaction = new PortfolioTransaction(portfolioId, PortfolioTransactionType.Sell, request.OccurredAt, null, request.Quantity * request.UnitPrice, source: FinancialSource.Manual);
        transaction.Items.Add(new PortfolioTransactionItem(transaction.Id, request.AssetId, request.Quantity, request.UnitPrice));
        db.PortfolioTransactions.Add(transaction); await db.SaveChangesAsync(ct);
        return Ok(new { transactionId = transaction.Id, totalAmount = transaction.TotalAmount });
    }

    [HttpPost("cash-flows")]
    public async Task<ActionResult<object>> CashFlow(Guid portfolioId, CashFlowRequest request, CancellationToken ct)
    {
        if (decimal.Round(request.Amount, 2) <= 0 || !Enum.IsDefined(request.Type) || !Enum.IsDefined(request.DatePrecision)) return BadRequest("Tipo, precisão da data e valor devem ser válidos.");
        if (request.Description?.Trim().Length > 300) return BadRequest("A descrição deve ter até 300 caracteres.");
        if (!await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct)) return NotFound();
        if (request.AssetId.HasValue && !await db.Assets.AnyAsync(x => x.Id == request.AssetId.Value, ct)) return BadRequest("Ativo não encontrado.");
        if (request.Type == PortfolioCashFlowType.Income && (!request.AssetId.HasValue || !await db.Assets.AnyAsync(x => x.Id == request.AssetId.Value, ct))) return BadRequest("Rendimento exige um ativo válido.");
        var flow = new PortfolioCashFlow(portfolioId, request.Type, request.Amount, request.OccurredAt, FinancialSource.Manual, request.AssetId, request.Description, request.DatePrecision);
        db.PortfolioCashFlows.Add(flow); await db.SaveChangesAsync(ct);
        return Ok(new { cashFlowId = flow.Id, amount = flow.Amount });
    }
}

public sealed record SellRequest(Guid AssetId, decimal Quantity, decimal UnitPrice, DateTimeOffset OccurredAt);
public sealed record CashFlowRequest(PortfolioCashFlowType Type, decimal Amount, DateTimeOffset OccurredAt, Guid? AssetId = null, string? Description = null, FinancialDatePrecision DatePrecision = FinancialDatePrecision.Exact);
public sealed record LedgerItemDto(DateTimeOffset OccurredAt, string Type, string? Ticker, decimal? Quantity, decimal? UnitPrice, decimal Amount, string Source, string DatePrecision);

public sealed record ConfirmContributionRequest(Guid? GuidanceId, IReadOnlyList<ContributionPurchaseRequest> Items, DateTimeOffset? OccurredAt = null);
public sealed record ContributionPurchaseRequest(Guid AssetId, decimal Quantity, decimal UnitPrice, string? CandidateKey = null);
