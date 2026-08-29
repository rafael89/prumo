using Lodira.Domain.Enums;

namespace Lodira.Domain.Entities;

public sealed class PortfolioAllocationStrategy
{
    private PortfolioAllocationStrategy() { }
    public PortfolioAllocationStrategy(Guid portfolioId, string name, string? templateKey, int version,
        PriorityLevel incomePriority, PriorityLevel growthPriority, PriorityLevel capitalPreservationPriority,
        ContributionPolicy contributionPolicy, decimal? maximumAssetWeight)
    {
        if (portfolioId == Guid.Empty) throw new ArgumentException("A carteira é obrigatória.", nameof(portfolioId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("O nome da estratégia é obrigatório.", nameof(name));
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        if (maximumAssetWeight is <= 0 or > 100) throw new ArgumentOutOfRangeException(nameof(maximumAssetWeight));
        Id = Guid.NewGuid(); PortfolioId = portfolioId; Name = name.Trim(); TemplateKey = string.IsNullOrWhiteSpace(templateKey) ? null : templateKey.Trim();
        Version = version; IncomePriority = incomePriority; GrowthPriority = growthPriority; CapitalPreservationPriority = capitalPreservationPriority;
        ContributionPolicy = contributionPolicy; MaximumAssetWeight = maximumAssetWeight; CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
    }

    public PortfolioAllocationStrategy(Guid portfolioId) : this(portfolioId, "Minha estratégia", null, 1, PriorityLevel.Medium, PriorityLevel.Medium, PriorityLevel.Medium, ContributionPolicy.CorrectImbalancesFirst, null) { }
    public Guid Id { get; private set; }
    public Guid PortfolioId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? TemplateKey { get; private set; }
    public int Version { get; private set; }
    public PriorityLevel IncomePriority { get; private set; }
    public PriorityLevel GrowthPriority { get; private set; }
    public PriorityLevel CapitalPreservationPriority { get; private set; }
    public ContributionPolicy ContributionPolicy { get; private set; }
    public decimal? MaximumAssetWeight { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public List<StrategyAllocation> Allocations { get; private set; } = new();
    public void Archive(DateTimeOffset? archivedAt = null) { ArchivedAt = archivedAt ?? DateTimeOffset.UtcNow; UpdatedAt = ArchivedAt.Value; }

    public static void ValidateAllocations(IEnumerable<(AssetClass AssetClass, decimal TargetPercentage)> allocations)
    {
        var items = allocations.ToList();
        if (items.Count == 0 || items.Any(x => !Enum.IsDefined(x.AssetClass) || x.TargetPercentage <= 0 || x.TargetPercentage > 100))
            throw new ArgumentException("As alocações devem conter classes válidas e percentuais entre 0 e 100%.", nameof(allocations));
        if (items.GroupBy(x => x.AssetClass).Any(g => g.Count() > 1) || Math.Abs(items.Sum(x => x.TargetPercentage) - 100m) > .01m)
            throw new ArgumentException("A soma das alocações deve ser 100% e as classes não podem se repetir.", nameof(allocations));
    }
}
