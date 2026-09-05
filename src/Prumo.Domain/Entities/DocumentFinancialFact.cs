using Prumo.Domain.Enums;

namespace Prumo.Domain.Entities;

public sealed class DocumentFinancialFact
{
    private DocumentFinancialFact() { }

    public DocumentFinancialFact(Guid documentId, Guid assetId, FinancialFactMetric metric, DateOnly referencePeriod, FinancialPeriodType periodType, decimal value, string? currency, string unit, string evidence, decimal confidence, DateTimeOffset? extractedAt = null)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("O documento é obrigatório.", nameof(documentId));
        if (assetId == Guid.Empty) throw new ArgumentException("O ativo é obrigatório.", nameof(assetId));
        if (string.IsNullOrWhiteSpace(unit)) throw new ArgumentException("A unidade é obrigatória.", nameof(unit));
        if (string.IsNullOrWhiteSpace(evidence)) throw new ArgumentException("A evidência é obrigatória.", nameof(evidence));
        if (confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(confidence));
        Id = Guid.NewGuid(); DocumentId = documentId; AssetId = assetId; Metric = metric; ReferencePeriod = referencePeriod; PeriodType = periodType;
        Value = value; Currency = string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant(); Unit = unit.Trim().ToLowerInvariant(); Evidence = evidence.Trim(); Confidence = confidence; ExtractedAt = (extractedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid AssetId { get; private set; }
    public FinancialFactMetric Metric { get; private set; }
    public DateOnly ReferencePeriod { get; private set; }
    public FinancialPeriodType PeriodType { get; private set; }
    public decimal Value { get; private set; }
    public string? Currency { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public string Evidence { get; private set; } = string.Empty;
    public decimal Confidence { get; private set; }
    public DateTimeOffset ExtractedAt { get; private set; }
    public Document? Document { get; private set; }
    public Asset? Asset { get; private set; }
}
