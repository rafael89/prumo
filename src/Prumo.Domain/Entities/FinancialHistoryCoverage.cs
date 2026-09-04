using Prumo.Domain.Enums;

namespace Prumo.Domain.Entities;

public sealed class FinancialHistoryCoverage
{
    private FinancialHistoryCoverage() { }

    public FinancialHistoryCoverage(Guid portfolioId, FinancialHistoryCoverageDimension dimension, DateOnly fromDate, DateOnly toDate, FinancialSource source, Guid? assetId = null, string? notes = null)
    {
        if (portfolioId == Guid.Empty) throw new ArgumentException("A carteira é obrigatória.", nameof(portfolioId));
        if (fromDate > toDate) throw new ArgumentException("O início deve ser anterior ou igual ao fim.", nameof(fromDate));
        if (assetId == Guid.Empty) throw new ArgumentException("O ativo informado é inválido.", nameof(assetId));
        Id = Guid.NewGuid(); PortfolioId = portfolioId; AssetId = assetId; Dimension = dimension; FromDate = fromDate; ToDate = toDate;
        Source = source; Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(); CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid PortfolioId { get; private set; }
    public Guid? AssetId { get; private set; }
    public FinancialHistoryCoverageDimension Dimension { get; private set; }
    public DateOnly FromDate { get; private set; }
    public DateOnly ToDate { get; private set; }
    public FinancialSource Source { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Asset? Asset { get; private set; }
}
