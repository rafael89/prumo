namespace Prumo.Domain.Entities;

public sealed class PortfolioTransactionItem
{
    private PortfolioTransactionItem() { }
    public PortfolioTransactionItem(Guid transactionId, Guid assetId, decimal quantity, decimal unitPrice)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (unitPrice <= 0) throw new ArgumentOutOfRangeException(nameof(unitPrice));
        Id = Guid.NewGuid(); PortfolioTransactionId = transactionId; AssetId = assetId; Quantity = quantity; UnitPrice = unitPrice;
    }
    public Guid Id { get; private set; }
    public Guid PortfolioTransactionId { get; private set; }
    public Guid AssetId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public PortfolioTransaction? PortfolioTransaction { get; private set; }
}
