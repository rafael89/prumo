namespace Prumo.Domain.Entities;

public sealed class AssetPrice
{
    private AssetPrice() { }
    public AssetPrice(Guid assetId, DateOnly priceDate, decimal close, string currency, string source, DateTimeOffset fetchedAt)
    {
        if (assetId == Guid.Empty) throw new ArgumentException("O ativo é obrigatório.", nameof(assetId));
        if (close <= 0) throw new ArgumentOutOfRangeException(nameof(close));
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("A moeda é obrigatória.", nameof(currency));
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("A fonte é obrigatória.", nameof(source));
        Id = Guid.NewGuid(); AssetId = assetId; PriceDate = priceDate; Close = close; Currency = currency.Trim().ToUpperInvariant(); Source = source.Trim(); FetchedAt = fetchedAt.ToUniversalTime();
    }
    public Guid Id { get; private set; }
    public Guid AssetId { get; private set; }
    public DateOnly PriceDate { get; private set; }
    public decimal Close { get; private set; }
    public string Currency { get; private set; } = "BRL";
    public string Source { get; private set; } = string.Empty;
    public DateTimeOffset FetchedAt { get; private set; }
    public Asset? Asset { get; private set; }
    public void Correct(decimal close, DateTimeOffset fetchedAt) { if (close <= 0) throw new ArgumentOutOfRangeException(nameof(close)); Close = close; FetchedAt = fetchedAt.ToUniversalTime(); }
}
