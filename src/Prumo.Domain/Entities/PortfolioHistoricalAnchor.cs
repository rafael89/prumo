using Prumo.Domain.Enums;

namespace Prumo.Domain.Entities;

public sealed class PortfolioHistoricalAnchor
{
    private PortfolioHistoricalAnchor() { }

    public PortfolioHistoricalAnchor(Guid portfolioId, DateOnly anchorDate, FinancialSource source, FinancialDatePrecision datePrecision, string? notes = null)
    {
        if (portfolioId == Guid.Empty) throw new ArgumentException("A carteira é obrigatória.", nameof(portfolioId));
        Id = Guid.NewGuid(); PortfolioId = portfolioId; AnchorDate = anchorDate; Source = source; DatePrecision = datePrecision;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(); CreatedAt = DateTimeOffset.UtcNow; UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid PortfolioId { get; private set; }
    public DateOnly AnchorDate { get; private set; }
    public FinancialSource Source { get; private set; }
    public FinancialDatePrecision DatePrecision { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public List<PortfolioHistoricalAnchorItem> Items { get; private set; } = new();

    public void Update(DateOnly anchorDate, FinancialSource source, FinancialDatePrecision datePrecision, string? notes)
    {
        AnchorDate = anchorDate; Source = source; DatePrecision = datePrecision;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(); UpdatedAt = DateTimeOffset.UtcNow;
    }
}
