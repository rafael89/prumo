namespace Prumo.Domain.Entities;

public sealed class PersistedContributionGuidance
{
    private PersistedContributionGuidance() { }

    public PersistedContributionGuidance(Guid portfolioId, decimal requestedAmount, string portfolioSnapshotKey, string targetAllocationKey,
        string intelligenceContextKey, string engineVersion, string provider, string model, string intelligenceAvailability, string reasoning, string metadataJson)
    {
        if (portfolioId == Guid.Empty) throw new ArgumentException("A carteira é obrigatória.", nameof(portfolioId));
        if (requestedAmount <= 0) throw new ArgumentOutOfRangeException(nameof(requestedAmount));
        Id = Guid.NewGuid(); PortfolioId = portfolioId; RequestedAmount = decimal.Round(requestedAmount, 2);
        PortfolioSnapshotKey = portfolioSnapshotKey; TargetAllocationKey = targetAllocationKey;
        IntelligenceContextKey = intelligenceContextKey;
        EngineVersion = engineVersion; Provider = provider; Model = model; IntelligenceAvailability = intelligenceAvailability;
        Reasoning = reasoning; MetadataJson = metadataJson; GeneratedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid PortfolioId { get; private set; }
    public decimal RequestedAmount { get; private set; }
    public string PortfolioSnapshotKey { get; private set; } = string.Empty;
    public string TargetAllocationKey { get; private set; } = string.Empty;
    public string IntelligenceContextKey { get; private set; } = string.Empty;
    public string EngineVersion { get; private set; } = string.Empty;
    public string Provider { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public string IntelligenceAvailability { get; private set; } = string.Empty;
    public string Reasoning { get; private set; } = string.Empty;
    public string MetadataJson { get; private set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; private set; }
    public List<PersistedContributionGuidanceAllocation> Allocations { get; private set; } = new();
    public void ReplaceAllocations(IEnumerable<PersistedContributionGuidanceAllocation> allocations) { Allocations.Clear(); Allocations.AddRange(allocations); }
    public void UpdateIntelligence(string provider, string model, string availability, string reasoning, string metadataJson)
    {
        Provider = provider; Model = model; IntelligenceAvailability = availability;
        Reasoning = reasoning; MetadataJson = metadataJson; GeneratedAt = DateTimeOffset.UtcNow;
    }
}
