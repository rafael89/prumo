namespace Lodira.Domain.Entities;

public sealed class PortfolioAllocationStrategy
{
    private PortfolioAllocationStrategy() { }
    public PortfolioAllocationStrategy(Guid portfolioId)
    { Id = Guid.NewGuid(); PortfolioId = portfolioId; CreatedAt = UpdatedAt = DateTimeOffset.UtcNow; }
    public Guid Id { get; private set; }
    public Guid PortfolioId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public List<StrategyAllocation> Allocations { get; private set; } = new();
}
