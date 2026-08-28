using Lodira.Domain.Enums;

namespace Lodira.Domain.Entities;

public sealed class StrategyAllocation
{
    private StrategyAllocation() { }
    public StrategyAllocation(Guid strategyId, AssetClass assetClass, decimal targetPercentage)
    { Id = Guid.NewGuid(); StrategyId = strategyId; AssetClass = assetClass; TargetPercentage = targetPercentage; }
    public Guid Id { get; private set; }
    public Guid StrategyId { get; private set; }
    public AssetClass AssetClass { get; private set; }
    public decimal TargetPercentage { get; private set; }
}
