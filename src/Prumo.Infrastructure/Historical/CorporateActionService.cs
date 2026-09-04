using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Infrastructure.Historical;

public sealed class CorporateActionService(PrumoDbContext db)
{
    public async Task<IReadOnlyList<CorporateActionDto>> List(Guid assetId, CancellationToken ct)
        => await db.AssetCorporateActions.AsNoTracking().Where(x => x.AssetId == assetId).OrderBy(x => x.EffectiveDate).Select(ToDto).ToListAsync(ct);

    public async Task<IReadOnlyList<HistoricalAssetDto>> HistoricalAssets(Guid portfolioId, CancellationToken ct)
    {
        var ids = await db.PortfolioPositions.Where(x => x.PortfolioId == portfolioId).Select(x => x.AssetId).ToListAsync(ct);
        ids.AddRange(await db.PortfolioHistoricalAnchorItems.Where(x => x.Anchor!.PortfolioId == portfolioId).Select(x => x.AssetId).ToListAsync(ct));
        ids.AddRange(await db.PortfolioTransactionItems.Where(x => x.PortfolioTransaction!.PortfolioId == portfolioId).Select(x => x.AssetId).ToListAsync(ct));
        return await db.Assets.AsNoTracking().Where(x => ids.Contains(x.Id)).OrderBy(x => x.Ticker).Select(x => new HistoricalAssetDto(x.Id, x.Ticker, x.Name)).ToListAsync(ct);
    }

    public Task<CorporateActionDto?> Get(Guid assetId, Guid id, CancellationToken ct)
        => db.AssetCorporateActions.AsNoTracking().Where(x => x.AssetId == assetId && x.Id == id).Select(ToDto).SingleOrDefaultAsync(ct);

    public async Task<CorporateActionDto> Create(Guid assetId, CorporateActionRequest request, CancellationToken ct)
    {
        await EnsureAsset(assetId, ct);
        var action = new AssetCorporateAction(assetId, request.Type, request.EffectiveDate, request.DatePrecision, request.FactorNumerator, request.FactorDenominator, request.Source, request.Notes, request.SourceReference);
        if (await IsDuplicate(action, null, ct)) throw new InvalidOperationException("Ação corporativa equivalente já cadastrada.");
        db.AssetCorporateActions.Add(action); await db.SaveChangesAsync(ct); return (await Get(assetId, action.Id, ct))!;
    }

    public async Task<CorporateActionDto?> Update(Guid assetId, Guid id, CorporateActionRequest request, CancellationToken ct)
    {
        var action = await db.AssetCorporateActions.SingleOrDefaultAsync(x => x.AssetId == assetId && x.Id == id, ct);
        if (action is null) return null;
        action.Update(request.Type, request.EffectiveDate, request.DatePrecision, request.FactorNumerator, request.FactorDenominator, request.Source, request.Notes, request.SourceReference);
        if (await IsDuplicate(action, id, ct)) throw new InvalidOperationException("Ação corporativa equivalente já cadastrada.");
        await db.SaveChangesAsync(ct); return await Get(assetId, id, ct);
    }

    public async Task<bool> Delete(Guid assetId, Guid id, CancellationToken ct)
    {
        var action = await db.AssetCorporateActions.SingleOrDefaultAsync(x => x.AssetId == assetId && x.Id == id, ct);
        if (action is null) return false;
        db.AssetCorporateActions.Remove(action); await db.SaveChangesAsync(ct); return true;
    }

    private async Task EnsureAsset(Guid assetId, CancellationToken ct)
    {
        if (!await db.Assets.AnyAsync(x => x.Id == assetId, ct)) throw new KeyNotFoundException("Ativo não encontrado.");
    }

    private Task<bool> IsDuplicate(AssetCorporateAction action, Guid? ignoredId, CancellationToken ct)
        => db.AssetCorporateActions.AnyAsync(x => x.AssetId == action.AssetId && x.Type == action.Type && x.EffectiveDate == action.EffectiveDate && x.FactorNumerator == action.FactorNumerator && x.FactorDenominator == action.FactorDenominator && x.Source == action.Source && (!ignoredId.HasValue || x.Id != ignoredId), ct);

    private static readonly System.Linq.Expressions.Expression<Func<AssetCorporateAction, CorporateActionDto>> ToDto = x => new(x.Id, x.AssetId, x.Type.ToString(), x.EffectiveDate, x.DatePrecision.ToString(), x.FactorNumerator, x.FactorDenominator, x.Source.ToString(), x.Notes, x.SourceReference, x.CreatedAt, x.UpdatedAt);
}

public sealed record CorporateActionRequest(CorporateActionType Type, DateOnly EffectiveDate, FinancialDatePrecision DatePrecision, decimal FactorNumerator, decimal FactorDenominator, FinancialSource Source, string? Notes = null, string? SourceReference = null);
public sealed record CorporateActionDto(Guid Id, Guid AssetId, string Type, DateOnly EffectiveDate, string DatePrecision, decimal FactorNumerator, decimal FactorDenominator, string Source, string? Notes, string? SourceReference, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record HistoricalAssetDto(Guid Id, string Ticker, string Name);
