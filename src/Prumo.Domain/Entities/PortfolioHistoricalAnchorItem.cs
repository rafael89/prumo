namespace Prumo.Domain.Entities;

public sealed class PortfolioHistoricalAnchorItem
{
    private PortfolioHistoricalAnchorItem() { }

    public PortfolioHistoricalAnchorItem(Guid anchorId, Guid assetId, decimal quantity, decimal? averageCost)
    {
        if (anchorId == Guid.Empty) throw new ArgumentException("A âncora é obrigatória.", nameof(anchorId));
        if (assetId == Guid.Empty) throw new ArgumentException("O ativo é obrigatório.", nameof(assetId));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (averageCost is < 0) throw new ArgumentOutOfRangeException(nameof(averageCost));
        Id = Guid.NewGuid(); AnchorId = anchorId; AssetId = assetId; Quantity = quantity; AverageCost = averageCost;
    }

    public Guid Id { get; private set; }
    public Guid AnchorId { get; private set; }
    public Guid AssetId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal? AverageCost { get; private set; }
    public Asset? Asset { get; private set; }
    public PortfolioHistoricalAnchor? Anchor { get; private set; }
}
