using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prumo.Infrastructure.MarketData;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/assets/{assetId:guid}/prices")]
public sealed class MarketDataController(PrumoDbContext db, MarketDataService prices) : ControllerBase
{
    [HttpGet("latest")]
    public async Task<ActionResult<PriceDto>> Latest(Guid assetId, CancellationToken ct)
    {
        var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assetId, ct);
        if (asset is null) return NotFound();
        var price = await prices.GetLatestAsync(assetId, ct);
        return price is null ? NotFound("Market data unavailable") : Ok(ToDto(price));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PriceDto>>> History(Guid assetId, DateOnly? from, DateOnly? to, int limit = 366, CancellationToken ct = default)
    {
        if (!await db.Assets.AnyAsync(x => x.Id == assetId, ct)) return NotFound();
        limit = Math.Clamp(limit, 1, 2000);
        var query = db.AssetPrices.AsNoTracking().Where(x => x.AssetId == assetId);
        if (from.HasValue) query = query.Where(x => x.PriceDate >= from.Value);
        if (to.HasValue) query = query.Where(x => x.PriceDate <= to.Value);
        return Ok(await query.OrderByDescending(x => x.PriceDate).ThenByDescending(x => x.FetchedAt).Take(limit).Select(x => ToDto(x)).ToListAsync(ct));
    }

    [HttpGet("as-of/{date}")]
    public async Task<ActionResult<PriceLookupDto>> AsOf(Guid assetId, DateOnly date, CancellationToken ct)
    {
        if (!await db.Assets.AnyAsync(x => x.Id == assetId, ct)) return NotFound();
        var price = await prices.GetAsOfAsync(assetId, date, ct);
        return Ok(new PriceLookupDto(date, price is null ? null : ToDto(price)));
    }

    private static PriceDto ToDto(Prumo.Domain.Entities.AssetPrice x) => new(x.PriceDate, x.Close, x.Currency, x.Source, x.FetchedAt);

    [HttpGet("/api/portfolios/{portfolioId:guid}/market-valuation")]
    public async Task<ActionResult<MarketValuationDto>> Valuation(Guid portfolioId, CancellationToken ct)
    {
        var positions = await db.PortfolioPositions.AsNoTracking().Include(x => x.Asset).Where(x => x.PortfolioId == portfolioId).ToListAsync(ct);
        if (positions.Count == 0 && !await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct)) return NotFound();
        var assetIds = positions.Select(x => x.AssetId).ToArray();
        var latest = await db.AssetPrices.AsNoTracking().Where(x => assetIds.Contains(x.AssetId)).OrderByDescending(x => x.PriceDate).ThenByDescending(x => x.FetchedAt).ToListAsync(ct);
        var byAsset = latest.GroupBy(x => x.AssetId).ToDictionary(x => x.Key, x => x.First());
        var items = positions.Select(x =>
        {
            var hasPrice = byAsset.TryGetValue(x.AssetId, out var price);
            return new MarketValuationItemDto(x.Asset?.Ticker ?? string.Empty, x.Quantity,
                hasPrice ? price!.Close : null,
                hasPrice ? x.Quantity * price!.Close : x.CurrentValue,
                hasPrice ? ToDto(price!) : null);
        }).ToList();
        var priced = items.Count(x => x.Value.HasValue);
        return Ok(new MarketValuationDto(items.Sum(x => x.Value ?? 0), priced == items.Count ? "Complete" : priced == 0 ? "Unavailable" : "Partial", priced, items.Count, items));
    }
}

public sealed record PriceDto(DateOnly PriceDate, decimal Close, string Currency, string Source, DateTimeOffset FetchedAt);
public sealed record PriceLookupDto(DateOnly RequestedDate, PriceDto? Price);
public sealed record MarketValuationDto(decimal KnownValue, string Coverage, int PricedAssets, int TotalAssets, IReadOnlyList<MarketValuationItemDto> Items);
public sealed record MarketValuationItemDto(string Ticker, decimal Quantity, decimal? Close, decimal? Value, PriceDto? Price);
