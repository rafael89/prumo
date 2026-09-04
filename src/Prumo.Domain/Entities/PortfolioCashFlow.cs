using Prumo.Domain.Enums;

namespace Prumo.Domain.Entities;

public sealed class PortfolioCashFlow
{
    private PortfolioCashFlow() { }

    public PortfolioCashFlow(Guid portfolioId, PortfolioCashFlowType type, decimal amount, DateTimeOffset occurredAt, FinancialSource source, Guid? assetId = null, string? description = null, FinancialDatePrecision datePrecision = FinancialDatePrecision.Exact)
    {
        if (portfolioId == Guid.Empty) throw new ArgumentException("A carteira é obrigatória.", nameof(portfolioId));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (type == PortfolioCashFlowType.Income && (assetId is null || assetId == Guid.Empty)) throw new ArgumentException("O ativo informado é obrigatório.", nameof(assetId));
        Id = Guid.NewGuid(); PortfolioId = portfolioId; Type = type; Amount = decimal.Round(amount, 2); OccurredAt = occurredAt.ToUniversalTime(); Source = source; AssetId = assetId; Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(); DatePrecision = datePrecision; CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid PortfolioId { get; private set; }
    public PortfolioCashFlowType Type { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public FinancialSource Source { get; private set; }
    public Guid? AssetId { get; private set; }
    public string? Description { get; private set; }
    public FinancialDatePrecision DatePrecision { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Asset? Asset { get; private set; }
}
