using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Application.Abstractions;

public sealed record MarketPricePoint(DateOnly PriceDate, decimal Close, string Currency = "BRL", string? Source = null);
public interface IMarketDataProvider
{
    string Name { get; }
    Task<IReadOnlyList<MarketPricePoint>> GetHistoryAsync(string ticker, DateOnly from, DateOnly to, AssetType assetType = AssetType.Stock, CancellationToken cancellationToken = default);
}
public sealed record AssetPriceLookup(DateOnly RequestedDate, AssetPrice? Price)
{
    public DateOnly? ActualPriceDate => Price?.PriceDate;
    public decimal? Close => Price?.Close;
    public string? Source => Price?.Source;
    public DateTimeOffset? FetchedAt => Price?.FetchedAt;
}
