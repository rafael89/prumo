using Prumo.Domain.Enums;

namespace Prumo.Domain.Entities;

public sealed class AssetCorporateAction
{
    private AssetCorporateAction() { }

    public AssetCorporateAction(Guid assetId, CorporateActionType type, DateOnly effectiveDate, FinancialDatePrecision datePrecision, decimal factorNumerator, decimal factorDenominator, FinancialSource source, string? notes = null, string? sourceReference = null)
    {
        Validate(assetId, type, factorNumerator, factorDenominator);
        Id = Guid.NewGuid(); AssetId = assetId; Type = type; EffectiveDate = effectiveDate; DatePrecision = datePrecision;
        FactorNumerator = factorNumerator; FactorDenominator = factorDenominator; Source = source;
        Notes = Clean(notes); SourceReference = Clean(sourceReference); CreatedAt = DateTimeOffset.UtcNow; UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid AssetId { get; private set; }
    public CorporateActionType Type { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public FinancialDatePrecision DatePrecision { get; private set; }
    public decimal FactorNumerator { get; private set; }
    public decimal FactorDenominator { get; private set; }
    public FinancialSource Source { get; private set; }
    public string? Notes { get; private set; }
    public string? SourceReference { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Asset? Asset { get; private set; }

    public void Update(CorporateActionType type, DateOnly effectiveDate, FinancialDatePrecision datePrecision, decimal factorNumerator, decimal factorDenominator, FinancialSource source, string? notes, string? sourceReference)
    {
        Validate(AssetId, type, factorNumerator, factorDenominator);
        Type = type; EffectiveDate = effectiveDate; DatePrecision = datePrecision; FactorNumerator = factorNumerator; FactorDenominator = factorDenominator; Source = source;
        Notes = Clean(notes); SourceReference = Clean(sourceReference); UpdatedAt = DateTimeOffset.UtcNow;
    }

    public static decimal Transform(decimal quantity, decimal numerator, decimal denominator) => quantity * numerator / denominator;

    private static void Validate(Guid assetId, CorporateActionType type, decimal numerator, decimal denominator)
    {
        if (assetId == Guid.Empty) throw new ArgumentException("O ativo é obrigatório.", nameof(assetId));
        if (numerator <= 0) throw new ArgumentOutOfRangeException(nameof(numerator));
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        if (numerator == denominator) throw new ArgumentException("O fator 1:1 não produz alteração.");
        if (type == CorporateActionType.StockSplit && numerator <= denominator) throw new ArgumentException("StockSplit deve aumentar a quantidade.", nameof(numerator));
        if (type == CorporateActionType.ReverseSplit && numerator >= denominator) throw new ArgumentException("ReverseSplit deve reduzir a quantidade.", nameof(numerator));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
