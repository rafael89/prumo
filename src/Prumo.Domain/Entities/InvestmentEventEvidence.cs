namespace Prumo.Domain.Entities;

public sealed class InvestmentEventEvidence
{
    public const int MaxReferenceLength = 1000;
    private InvestmentEventEvidence() { }
    public InvestmentEventEvidence(Guid investmentEventId, Guid documentAnalysisId, Guid documentId, string reference)
    {
        Id = Guid.NewGuid(); InvestmentEventId = investmentEventId; DocumentAnalysisId = documentAnalysisId; DocumentId = documentId;
        Reference = string.IsNullOrWhiteSpace(reference) ? "Documento analisado" : reference.Trim();
        // This is a short excerpt; the complete evidence remains in RadarDetails.
        if (Reference.Length > MaxReferenceLength)
        {
            var length = char.IsHighSurrogate(Reference[MaxReferenceLength - 1]) ? MaxReferenceLength - 1 : MaxReferenceLength;
            Reference = Reference[..length];
        }
    }
    public Guid Id { get; private set; }
    public Guid InvestmentEventId { get; private set; }
    public Guid DocumentAnalysisId { get; private set; }
    public Guid DocumentId { get; private set; }
    public string Reference { get; private set; } = string.Empty;
}
