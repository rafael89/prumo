using System.Security.Cryptography;
using System.Text;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Entities;

public sealed class InvestmentEvent
{
    private InvestmentEvent() { }

    public InvestmentEvent(Guid documentAnalysisId, InvestmentEventType eventType, DateTimeOffset? occurredAt, string summary, IEnumerable<string> facts, int version = 1)
    {
        if (documentAnalysisId == Guid.Empty) throw new ArgumentException("A análise é obrigatória.", nameof(documentAnalysisId));
        if (string.IsNullOrWhiteSpace(summary)) throw new ArgumentException("O resumo é obrigatório.", nameof(summary));
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        Id = Guid.NewGuid(); DocumentAnalysisId = documentAnalysisId; EventType = eventType; OccurredAt = occurredAt;
        Summary = summary.Trim(); Facts = facts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList(); Version = version; CreatedAt = DateTimeOffset.UtcNow;
        Fingerprint = CreateFingerprint(eventType, occurredAt, Summary, Facts);
    }

    public Guid Id { get; private set; }
    public Guid DocumentAnalysisId { get; private set; }
    public InvestmentEventType EventType { get; private set; }
    public DateTimeOffset? OccurredAt { get; private set; }
    public string Summary { get; private set; } = string.Empty;
    public List<string> Facts { get; private set; } = new();
    public int Version { get; private set; }
    public string Fingerprint { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DocumentAnalysis? DocumentAnalysis { get; private set; }
    public List<Asset> AffectedAssets { get; private set; } = new();
    public List<InvestmentEventEvidence> Evidence { get; private set; } = new();

    private static string CreateFingerprint(InvestmentEventType type, DateTimeOffset? occurredAt, string summary, IEnumerable<string> facts)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{type}|{occurredAt:O}|{summary}|{string.Join("|", facts)}"))).ToLowerInvariant();
}
