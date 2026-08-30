using Lodira.Domain.Enums;

namespace Lodira.Domain.Entities;

public sealed class PortfolioImportSession
{
    private PortfolioImportSession() { }
    public PortfolioImportSession(Guid portfolioId, PortfolioImportSource source, string originalFileName, string fileHash, string normalizedJson, DateTimeOffset? referenceDate)
    { Id = Guid.NewGuid(); PortfolioId = portfolioId; Source = source; OriginalFileName = originalFileName; FileHash = fileHash; NormalizedJson = normalizedJson; ReferenceDate = referenceDate?.ToUniversalTime(); Status = PortfolioImportStatus.Previewed; CreatedAt = DateTimeOffset.UtcNow; }
    public Guid Id { get; private set; }
    public Guid PortfolioId { get; private set; }
    public PortfolioImportSource Source { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string FileHash { get; private set; } = string.Empty;
    public string NormalizedJson { get; private set; } = string.Empty;
    public DateTimeOffset? ReferenceDate { get; private set; }
    public PortfolioImportStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? PortfolioSnapshotKey { get; private set; }
    public string? Error { get; private set; }
    public void MarkConfirmed() { Status = PortfolioImportStatus.Confirmed; ConfirmedAt = DateTimeOffset.UtcNow; }
    public void MarkCompleted() { Status = PortfolioImportStatus.Completed; CompletedAt = DateTimeOffset.UtcNow; }
    public void SetPortfolioSnapshotKey(string snapshotKey) { PortfolioSnapshotKey = string.IsNullOrWhiteSpace(snapshotKey) ? throw new ArgumentException("A chave do snapshot é obrigatória.", nameof(snapshotKey)) : snapshotKey.Trim(); }
    public void MarkFailed(string error) { Status = PortfolioImportStatus.Failed; Error = error; }
}
