namespace Lodira.Domain.Entities;

public sealed class TargetAllocation
{
    private TargetAllocation() { }
    public TargetAllocation(Guid strategyId, Guid assetId, decimal targetPercentage)
    { Id = Guid.NewGuid(); StrategyId = strategyId; AssetId = assetId; TargetPercentage = targetPercentage; }
    public Guid Id { get; private set; }
    public Guid StrategyId { get; private set; }
    public Guid AssetId { get; private set; }
    public decimal TargetPercentage { get; private set; }
    public Asset? Asset { get; private set; }
}
