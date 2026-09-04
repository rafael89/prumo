using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Infrastructure.Historical;

public sealed class HistoricalAnchorService(PrumoDbContext db)
{
    public async Task<IReadOnlyList<HistoricalAnchorDto>> List(Guid portfolioId, CancellationToken ct)
        => (await Query(portfolioId).OrderByDescending(x => x.AnchorDate).ToListAsync(ct)).Select(Map).ToList();

    public async Task<HistoricalAnchorDto?> Get(Guid portfolioId, Guid anchorId, CancellationToken ct)
        => await Query(portfolioId).SingleOrDefaultAsync(x => x.Id == anchorId, ct) is { } anchor ? Map(anchor) : null;

    public async Task<AnchorValidationDto?> Validate(Guid portfolioId, Guid anchorId, CoverageService coverage, CancellationToken ct)
    {
        var anchor = await db.PortfolioHistoricalAnchors.AsNoTracking().Include(x => x.Items).ThenInclude(x => x.Asset).SingleOrDefaultAsync(x => x.Id == anchorId && x.PortfolioId == portfolioId, ct);
        if (anchor is null) return null;
        var positions = await db.PortfolioPositions.AsNoTracking().Where(x => x.PortfolioId == portfolioId).ToDictionaryAsync(x => x.AssetId, ct);
        var transactions = await db.PortfolioTransactions.AsNoTracking().Include(x => x.Items).Where(x => x.PortfolioId == portfolioId && x.OccurredAt.Date > anchor.AnchorDate.ToDateTime(TimeOnly.MinValue)).ToListAsync(ct);
        var result = new List<AnchorValidationItemDto>();
        foreach (var item in anchor.Items)
        {
            var expected = item.Quantity + transactions.SelectMany(x => x.Items.Where(i => i.AssetId == item.AssetId).Select(i => x.Type == PortfolioTransactionType.Buy ? i.Quantity : -i.Quantity)).Sum();
            var current = positions.GetValueOrDefault(item.AssetId)?.Quantity;
            var transactionCoverage = await coverage.Resolve(portfolioId, FinancialHistoryCoverageDimension.Transactions, anchor.AnchorDate, DateOnly.FromDateTime(DateTime.UtcNow), item.AssetId, ct);
            var corporateActionCoverage = await coverage.Resolve(portfolioId, FinancialHistoryCoverageDimension.CorporateActions, anchor.AnchorDate, DateOnly.FromDateTime(DateTime.UtcNow), item.AssetId, ct);
            var status = transactionCoverage.Status != "Complete" ? "InsufficientCoverage" : expected == current && (corporateActionCoverage.Status == "Complete" || expected == 0) ? "KnownLedgerMatch" : corporateActionCoverage.Status != "Complete" ? "InsufficientCoverage" : "KnownLedgerMismatch";
            result.Add(new(item.AssetId, item.Asset?.Ticker ?? "", expected, current, status));
        }
        return new(anchor.Id, result);
    }

    public async Task<HistoricalAnchorDto> Create(Guid portfolioId, HistoricalAnchorRequest request, CancellationToken ct)
    {
        await ValidatePortfolio(portfolioId, ct);
        if (await db.PortfolioHistoricalAnchors.AnyAsync(x => x.PortfolioId == portfolioId && x.AnchorDate == request.AnchorDate, ct)) throw new InvalidOperationException("Já existe uma âncora nesta data.");
        var anchor = new PortfolioHistoricalAnchor(portfolioId, request.AnchorDate, request.Source, request.DatePrecision, request.Notes);
        await ReplaceItems(anchor, portfolioId, request.Items, ct); db.PortfolioHistoricalAnchors.Add(anchor); await db.SaveChangesAsync(ct);
        return Map(await Query(portfolioId).SingleAsync(x => x.Id == anchor.Id, ct));
    }

    public async Task<HistoricalAnchorDto?> Update(Guid portfolioId, Guid anchorId, HistoricalAnchorRequest request, CancellationToken ct)
    {
        var anchor = await db.PortfolioHistoricalAnchors.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == anchorId && x.PortfolioId == portfolioId, ct);
        if (anchor is null) return null;
        if (await db.PortfolioHistoricalAnchors.AnyAsync(x => x.Id != anchorId && x.PortfolioId == portfolioId && x.AnchorDate == request.AnchorDate, ct)) throw new InvalidOperationException("Já existe uma âncora nesta data.");
        anchor.Update(request.AnchorDate, request.Source, request.DatePrecision, request.Notes);
        db.PortfolioHistoricalAnchorItems.RemoveRange(anchor.Items); await ReplaceItems(anchor, portfolioId, request.Items, ct); await db.SaveChangesAsync(ct);
        return Map(await Query(portfolioId).SingleAsync(x => x.Id == anchorId, ct));
    }

    public async Task<bool> Delete(Guid portfolioId, Guid anchorId, CancellationToken ct)
    {
        var anchor = await db.PortfolioHistoricalAnchors.SingleOrDefaultAsync(x => x.Id == anchorId && x.PortfolioId == portfolioId, ct);
        if (anchor is null) return false; db.PortfolioHistoricalAnchors.Remove(anchor); await db.SaveChangesAsync(ct); return true;
    }

    private async Task ReplaceItems(PortfolioHistoricalAnchor anchor, Guid portfolioId, IReadOnlyList<HistoricalAnchorItemRequest>? items, CancellationToken ct)
    {
        if (items is null || items.Count == 0) throw new ArgumentException("Informe ao menos uma posição.");
        if (items.Select(x => x.AssetId).Distinct().Count() != items.Count) throw new ArgumentException("Não repita o mesmo ativo na âncora.");
        var ids = items.Select(x => x.AssetId).ToHashSet();
        if (items.Any(x => x.Quantity <= 0 || x.AverageCost is < 0)) throw new ArgumentException("Quantidade deve ser positiva e custo não pode ser negativo.");
        var assets = await db.Assets.Where(x => ids.Contains(x.Id) && x.IsActive).Select(x => x.Id).ToListAsync(ct);
        if (assets.Count != ids.Count) throw new ArgumentException("Todos os ativos devem existir e estar ativos.");
        foreach (var item in items) anchor.Items.Add(new PortfolioHistoricalAnchorItem(anchor.Id, item.AssetId, item.Quantity, item.AverageCost));
    }

    private IQueryable<PortfolioHistoricalAnchor> Query(Guid portfolioId)
        => db.PortfolioHistoricalAnchors.AsNoTracking().Include(x => x.Items).ThenInclude(x => x.Asset).Where(x => x.PortfolioId == portfolioId);

    private async Task ValidatePortfolio(Guid portfolioId, CancellationToken ct)
    { if (!await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct)) throw new KeyNotFoundException("Carteira não encontrada."); }

    private static HistoricalAnchorDto Map(PortfolioHistoricalAnchor x)
        => new(x.Id, x.AnchorDate, x.Source.ToString(), x.DatePrecision.ToString(), x.Notes, x.CreatedAt, x.UpdatedAt, x.Items.Select(i => new HistoricalAnchorItemDto(i.AssetId, i.Asset?.Ticker ?? "", i.Quantity, i.AverageCost)).ToList());
}

public sealed record HistoricalAnchorRequest(DateOnly AnchorDate, Prumo.Domain.Enums.FinancialSource Source, Prumo.Domain.Enums.FinancialDatePrecision DatePrecision, IReadOnlyList<HistoricalAnchorItemRequest> Items, string? Notes = null);
public sealed record HistoricalAnchorItemRequest(Guid AssetId, decimal Quantity, decimal? AverageCost);
public sealed record HistoricalAnchorDto(Guid Id, DateOnly AnchorDate, string Source, string DatePrecision, string? Notes, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<HistoricalAnchorItemDto> Items);
public sealed record HistoricalAnchorItemDto(Guid AssetId, string Ticker, decimal Quantity, decimal? AverageCost);
public sealed record AnchorValidationDto(Guid AnchorId, IReadOnlyList<AnchorValidationItemDto> Items);
public sealed record AnchorValidationItemDto(Guid AssetId, string Ticker, decimal ExpectedQuantity, decimal? CurrentQuantity, string Status);
