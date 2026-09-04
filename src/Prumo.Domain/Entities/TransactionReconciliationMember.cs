namespace Prumo.Domain.Entities;

public sealed class TransactionReconciliationMember
{
    private TransactionReconciliationMember() { }

    public TransactionReconciliationMember(Guid reconciliationId, Guid portfolioTransactionId)
    {
        Id = Guid.NewGuid(); ReconciliationId = reconciliationId; PortfolioTransactionId = portfolioTransactionId;
    }

    public Guid Id { get; private set; }
    public Guid ReconciliationId { get; private set; }
    public Guid PortfolioTransactionId { get; private set; }
    public TransactionReconciliation? Reconciliation { get; private set; }
    public PortfolioTransaction? PortfolioTransaction { get; private set; }
}
