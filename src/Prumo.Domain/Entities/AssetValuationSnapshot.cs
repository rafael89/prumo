using Prumo.Domain.Enums;

namespace Prumo.Domain.Entities;

public sealed class AssetValuationSnapshot
{
    private AssetValuationSnapshot() { }

    public AssetValuationSnapshot(Guid assetId, DateOnly referenceDate, FinancialDataSource source = FinancialDataSource.Unknown, string? sourceReference = null, DateTimeOffset? observedAt = null, decimal? priceEarnings = null, decimal? priceBook = null, decimal? evEbitda = null, decimal? dividendYield = null)
    {
        Id = Guid.NewGuid();
        AssetId = assetId;
        ReferenceDate = referenceDate;
        Source = source;
        SourceReference = string.IsNullOrWhiteSpace(sourceReference) ? null : sourceReference.Trim();
        ObservedAt = (observedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        PriceEarnings = priceEarnings;
        PriceBook = priceBook;
        EvEbitda = evEbitda;
        DividendYield = dividendYield;
    }

    public Guid Id { get; private set; }
    public Guid AssetId { get; private set; }
    public Asset? Asset { get; private set; }
    public DateOnly ReferenceDate { get; private set; }
    public FinancialDataSource Source { get; private set; }
    public string? SourceReference { get; private set; }
    public DateTimeOffset ObservedAt { get; private set; }
    public decimal? PriceEarnings { get; private set; }
    public decimal? PriceBook { get; private set; }
    public decimal? EvEbitda { get; private set; }
    public decimal? DividendYield { get; private set; }

    public void ReplaceValues(DateTimeOffset observedAt, string? sourceReference, decimal? priceEarnings, decimal? priceBook, decimal? evEbitda, decimal? dividendYield)
    {
        ObservedAt = observedAt.ToUniversalTime();
        SourceReference = string.IsNullOrWhiteSpace(sourceReference) ? null : sourceReference.Trim();
        PriceEarnings = priceEarnings;
        PriceBook = priceBook;
        EvEbitda = evEbitda;
        DividendYield = dividendYield;
    }
}
