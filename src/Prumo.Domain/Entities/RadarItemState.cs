using Prumo.Domain.Enums;

namespace Prumo.Domain.Entities;

public sealed class RadarItemState
{
    private RadarItemState() { }
    public RadarItemState(Guid documentAnalysisId) : this(Guid.Empty, documentAnalysisId) { }
    public RadarItemState(Guid portfolioId, Guid documentAnalysisId)
    {
        if (documentAnalysisId == Guid.Empty) throw new ArgumentException("A análise é obrigatória.", nameof(documentAnalysisId));
        Id = Guid.NewGuid(); PortfolioId = portfolioId; DocumentAnalysisId = documentAnalysisId;
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid PortfolioId { get; private set; }
    public Guid DocumentAnalysisId { get; private set; }
    public RadarItemStatus Status { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public void SetStatus(RadarItemStatus status)
    {
        var now = DateTimeOffset.UtcNow; Status = status;
        if (status == RadarItemStatus.Unread) { ReadAt = null; ResolvedAt = null; }
        else if (status == RadarItemStatus.Read) { ReadAt ??= now; ResolvedAt = null; }
        else { ReadAt ??= now; ResolvedAt ??= now; }
        UpdatedAt = now;
    }
}
