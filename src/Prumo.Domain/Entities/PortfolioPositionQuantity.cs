namespace Prumo.Domain.Entities;

public sealed class PortfolioPositionQuantity(Guid portfolioId, Guid assetId, DateTimeOffset recordedAt, decimal quantity)
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PortfolioId { get; private set; } = portfolioId;
    public Guid AssetId { get; private set; } = assetId;
    public DateTimeOffset RecordedAt { get; private set; } = recordedAt.ToUniversalTime();
    public decimal Quantity { get; private set; } = quantity;
}
