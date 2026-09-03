using Prumo.Domain.Entities;

namespace Prumo.Domain.Tests;

public sealed class AssetPriceTests
{
    [Fact]
    public void Stores_market_date_separately_from_fetch_date_and_allows_correction()
    {
        var price = new AssetPrice(Guid.NewGuid(), new DateOnly(2026, 9, 2), 142.31m, "brl", "Brapi", DateTimeOffset.Parse("2026-09-03T01:10:00Z"));
        price.Correct(143m, DateTimeOffset.Parse("2026-09-04T01:10:00Z"));
        Assert.Equal(new DateOnly(2026, 9, 2), price.PriceDate);
        Assert.Equal(143m, price.Close);
        Assert.Equal("BRL", price.Currency);
        Assert.Equal(DateTimeOffset.Parse("2026-09-04T01:10:00Z"), price.FetchedAt);
    }

    [Fact]
    public void Rejects_non_positive_market_price()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new AssetPrice(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), 0, "BRL", "Brapi", DateTimeOffset.UtcNow));
}
