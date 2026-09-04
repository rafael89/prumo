using Prumo.Domain.Enums;

namespace Prumo.Domain.Entities;

public sealed class PortfolioCashAnchor
{
    private PortfolioCashAnchor() { }

    public PortfolioCashAnchor(Guid portfolioId, DateOnly anchorDate, decimal balance, FinancialSource source, FinancialDatePrecision datePrecision, string currency = "BRL", string? notes = null)
    {
        if (portfolioId == Guid.Empty) throw new ArgumentException("A carteira é obrigatória.", nameof(portfolioId));
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3) throw new ArgumentException("A moeda deve possuir 3 caracteres.", nameof(currency));
        Id = Guid.NewGuid(); PortfolioId = portfolioId; AnchorDate = anchorDate; Balance = decimal.Round(balance, 2); Currency = currency.Trim().ToUpperInvariant(); Source = source; DatePrecision = datePrecision; Notes = Clean(notes); CreatedAt = DateTimeOffset.UtcNow; UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid PortfolioId { get; private set; }
    public DateOnly AnchorDate { get; private set; }
    public decimal Balance { get; private set; }
    public string Currency { get; private set; } = "BRL";
    public FinancialSource Source { get; private set; }
    public FinancialDatePrecision DatePrecision { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(DateOnly anchorDate, decimal balance, FinancialSource source, FinancialDatePrecision datePrecision, string currency, string? notes)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3) throw new ArgumentException("A moeda deve possuir 3 caracteres.", nameof(currency));
        AnchorDate = anchorDate; Balance = decimal.Round(balance, 2); Currency = currency.Trim().ToUpperInvariant(); Source = source; DatePrecision = datePrecision; Notes = Clean(notes); UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
