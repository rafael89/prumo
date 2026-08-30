namespace Lodira.Domain.Entities;

public sealed class PersistedContributionGuidanceAllocation
{
    private PersistedContributionGuidanceAllocation() { }
    public PersistedContributionGuidanceAllocation(Guid guidanceId, Guid assetId, decimal amount, string reason, decimal confidence, string evidenceJson)
    {
        Id = Guid.NewGuid(); ContributionGuidanceId = guidanceId; AssetId = assetId; Amount = decimal.Round(amount, 2);
        Reason = reason; Confidence = confidence; EvidenceJson = evidenceJson;
    }
    public Guid Id { get; private set; }
    public Guid ContributionGuidanceId { get; private set; }
    public Guid AssetId { get; private set; }
    public decimal Amount { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public decimal Confidence { get; private set; }
    public string EvidenceJson { get; private set; } = string.Empty;
    public PersistedContributionGuidance? ContributionGuidance { get; private set; }
}
