namespace Prumo.Domain.Entities;

using Prumo.Domain.Enums;

public sealed class PersistedContributionGuidanceAllocation
{
    private PersistedContributionGuidanceAllocation() { }
    public PersistedContributionGuidanceAllocation(Guid guidanceId, Guid assetId, decimal amount, string reason, decimal confidence, string evidenceJson)
    {
        if (assetId == Guid.Empty) throw new ArgumentException("O ativo é obrigatório.", nameof(assetId));
        Id = Guid.NewGuid(); ContributionGuidanceId = guidanceId; AssetId = assetId; Origin = "KnownCatalog"; Amount = decimal.Round(amount, 2);
        Reason = reason; Confidence = confidence; EvidenceJson = evidenceJson;
    }
    public PersistedContributionGuidanceAllocation(Guid guidanceId, string candidateKey, string provider, string ticker, string name, AssetClass assetClass, AssetType assetType, string? exchange, string? currency, string? sector, string? sourceReference, decimal amount, string reason, decimal confidence, string evidenceJson)
    {
        if (string.IsNullOrWhiteSpace(candidateKey) || string.IsNullOrWhiteSpace(provider)) throw new ArgumentException("O snapshot externo é obrigatório.");
        Id = Guid.NewGuid(); ContributionGuidanceId = guidanceId; Origin = "ExternalDiscovery"; CandidateKey = candidateKey; Provider = provider; Ticker = ticker; Name = name; AssetClass = assetClass; AssetType = assetType; Exchange = exchange; Currency = currency; Sector = sector; SourceReference = sourceReference; Amount = decimal.Round(amount, 2);
        Reason = reason; Confidence = confidence; EvidenceJson = evidenceJson;
    }
    public Guid Id { get; private set; }
    public Guid ContributionGuidanceId { get; private set; }
    public Guid? AssetId { get; private set; }
    public string Origin { get; private set; } = "KnownCatalog";
    public string? CandidateKey { get; private set; }
    public string? Provider { get; private set; }
    public string? Ticker { get; private set; }
    public string? Name { get; private set; }
    public AssetClass? AssetClass { get; private set; }
    public AssetType? AssetType { get; private set; }
    public string? Exchange { get; private set; }
    public string? Currency { get; private set; }
    public string? Sector { get; private set; }
    public string? SourceReference { get; private set; }
    public decimal Amount { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public decimal Confidence { get; private set; }
    public string EvidenceJson { get; private set; } = string.Empty;
    public PersistedContributionGuidance? ContributionGuidance { get; private set; }
}
