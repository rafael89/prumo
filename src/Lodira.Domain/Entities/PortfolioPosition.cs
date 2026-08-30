namespace Lodira.Domain.Entities;

public sealed class PortfolioPosition
{
    private PortfolioPosition()
    {
    }

    public PortfolioPosition(Guid portfolioId, Guid assetId, decimal quantity, decimal averagePrice, decimal? currentPrice)
    {
        Id = Guid.NewGuid();
        PortfolioId = portfolioId;
        AssetId = assetId;
        Update(quantity, averagePrice, currentPrice);
    }

    public Guid Id { get; private set; }
    public Guid PortfolioId { get; private set; }
    public Guid AssetId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal AveragePrice { get; private set; }
    public decimal? CurrentPrice { get; private set; }
    public decimal? CurrentValue { get; private set; }
    public decimal PortfolioWeight { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Asset? Asset { get; private set; }

    public void Update(decimal quantity, decimal averagePrice, decimal? currentPrice)
    {
        if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (averagePrice < 0) throw new ArgumentOutOfRangeException(nameof(averagePrice));
        if (currentPrice is < 0) throw new ArgumentOutOfRangeException(nameof(currentPrice));

        Quantity = quantity;
        AveragePrice = averagePrice;
        CurrentPrice = currentPrice;
        CurrentValue = CalculateCurrentValue(quantity, currentPrice);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetPortfolioWeight(decimal totalPortfolioValue)
    {
        PortfolioWeight = totalPortfolioValue > 0 && CurrentValue.HasValue
            ? CurrentValue.Value / totalPortfolioValue * 100
            : 0;
    }

    public void ApplyBuy(decimal quantity, decimal unitPrice)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (unitPrice <= 0) throw new ArgumentOutOfRangeException(nameof(unitPrice));
        var newQuantity = Quantity + quantity;
        var newAveragePrice = ((Quantity * AveragePrice) + (quantity * unitPrice)) / newQuantity;
        Update(newQuantity, newAveragePrice, CurrentPrice);
    }

    public static decimal? CalculateCurrentValue(decimal quantity, decimal? currentPrice)
    {
        return currentPrice.HasValue ? quantity * currentPrice.Value : null;
    }
}
