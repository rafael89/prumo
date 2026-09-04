using Prumo.Domain.Enums;

namespace Prumo.Domain.Entities;

public sealed class TransactionReconciliation
{
    private TransactionReconciliation() { }

    public TransactionReconciliation(Guid portfolioId, TransactionReconciliationStatus status, string? notes = null)
    {
        if (portfolioId == Guid.Empty) throw new ArgumentException("A carteira é obrigatória.", nameof(portfolioId));
        Id = Guid.NewGuid(); PortfolioId = portfolioId; Status = status; Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(); CreatedAt = DateTimeOffset.UtcNow; UpdatedAt = CreatedAt; ResolvedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid PortfolioId { get; private set; }
    public TransactionReconciliationStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public List<TransactionReconciliationMember> Members { get; private set; } = new();

    public void Update(TransactionReconciliationStatus status, string? notes)
    {
        Status = status; Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(); UpdatedAt = DateTimeOffset.UtcNow; ResolvedAt = UpdatedAt;
    }
}
