namespace Prumo.Domain.Entities;

public sealed class InvestmentEventEvidence
{
    private InvestmentEventEvidence() { }
    public InvestmentEventEvidence(Guid investmentEventId, Guid documentAnalysisId, Guid documentId, string reference)
    { Id = Guid.NewGuid(); InvestmentEventId = investmentEventId; DocumentAnalysisId = documentAnalysisId; DocumentId = documentId; Reference = string.IsNullOrWhiteSpace(reference) ? "Documento analisado" : reference.Trim(); }
    public Guid Id { get; private set; }
    public Guid InvestmentEventId { get; private set; }
    public Guid DocumentAnalysisId { get; private set; }
    public Guid DocumentId { get; private set; }
    public string Reference { get; private set; } = string.Empty;
}
