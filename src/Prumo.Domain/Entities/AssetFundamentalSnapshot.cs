using Prumo.Domain.Enums;

namespace Prumo.Domain.Entities;

public sealed class AssetFundamentalSnapshot
{
    private AssetFundamentalSnapshot() { }

    public AssetFundamentalSnapshot(Guid assetId, DateOnly referenceDate, FinancialPeriodType periodType, FinancialDataSource source = FinancialDataSource.Unknown, string? sourceReference = null, DateTimeOffset? observedAt = null, decimal? revenue = null, decimal? netIncome = null, decimal? equity = null, decimal? totalDebt = null, decimal? cash = null, decimal? ebitda = null, decimal? roe = null, decimal? roic = null, decimal? netMargin = null)
    {
        Id = Guid.NewGuid();
        AssetId = assetId;
        ReferenceDate = referenceDate;
        PeriodType = periodType;
        Source = source;
        SourceReference = string.IsNullOrWhiteSpace(sourceReference) ? null : sourceReference.Trim();
        ObservedAt = (observedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        Revenue = revenue;
        NetIncome = netIncome;
        Equity = equity;
        TotalDebt = totalDebt;
        Cash = cash;
        Ebitda = ebitda;
        Roe = roe;
        Roic = roic;
        NetMargin = netMargin;
    }

    public Guid Id { get; private set; }
    public Guid AssetId { get; private set; }
    public Asset? Asset { get; private set; }
    public DateOnly ReferenceDate { get; private set; }
    public FinancialPeriodType PeriodType { get; private set; }
    public FinancialDataSource Source { get; private set; }
    public string? SourceReference { get; private set; }
    public DateTimeOffset ObservedAt { get; private set; }
    public decimal? Revenue { get; private set; }
    public decimal? NetIncome { get; private set; }
    public decimal? Equity { get; private set; }
    public decimal? TotalDebt { get; private set; }
    public decimal? Cash { get; private set; }
    public decimal? Ebitda { get; private set; }
    public decimal? Roe { get; private set; }
    public decimal? Roic { get; private set; }
    public decimal? NetMargin { get; private set; }

    public void ReplaceValues(DateTimeOffset observedAt, string? sourceReference, decimal? roe, decimal? netMargin)
    {
        ObservedAt = observedAt.ToUniversalTime();
        SourceReference = string.IsNullOrWhiteSpace(sourceReference) ? null : sourceReference.Trim();
        Roe = roe;
        NetMargin = netMargin;
    }
}
