using Lodira.Domain.Enums;

namespace Lodira.Domain.Entities;

public sealed class TargetAllocation
{
    private TargetAllocation() { }
    public TargetAllocation(Guid portfolioId, AssetClass assetClass, decimal targetPercentage)
    {
        if (portfolioId == Guid.Empty) throw new ArgumentException("A carteira é obrigatória.", nameof(portfolioId));
        if (!Enum.IsDefined(assetClass)) throw new ArgumentOutOfRangeException(nameof(assetClass));
        if (targetPercentage < 0 || targetPercentage > 100) throw new ArgumentOutOfRangeException(nameof(targetPercentage));
        Id = Guid.NewGuid(); PortfolioId = portfolioId; AssetClass = assetClass; TargetPercentage = targetPercentage;
    }
    public Guid Id { get; private set; }
    public Guid PortfolioId { get; private set; }
    public AssetClass AssetClass { get; private set; }
    public decimal TargetPercentage { get; private set; }
}
