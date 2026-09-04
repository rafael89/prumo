using Prumo.Domain.Enums;

namespace Prumo.Domain.Entities;

public sealed class AssetCurrencyExposure
{
    private AssetCurrencyExposure() { }

    public AssetCurrencyExposure(Guid assetId, string currencyCode, decimal? percentage, ExposureSource source = ExposureSource.Unknown, string? sourceReference = null, DateTimeOffset? observedAt = null, bool isDerived = false)
    {
        Id = Guid.NewGuid();
        AssetId = assetId;
        CurrencyCode = ValidateCode(currencyCode);
        Percentage = percentage is < 0 or > 100 ? throw new ArgumentOutOfRangeException(nameof(percentage)) : percentage;
        Source = source;
        SourceReference = string.IsNullOrWhiteSpace(sourceReference) ? null : sourceReference.Trim();
        ObservedAt = observedAt?.ToUniversalTime();
        IsDerived = isDerived;
    }

    public Guid Id { get; private set; }
    public Guid AssetId { get; private set; }
    public Asset? Asset { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal? Percentage { get; private set; }
    public ExposureSource Source { get; private set; }
    public string? SourceReference { get; private set; }
    public DateTimeOffset? ObservedAt { get; private set; }
    public bool IsDerived { get; private set; }

    private static string ValidateCode(string value)
    {
        value = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (value.Length != 3 || value.Any(character => character is < 'A' or > 'Z')) throw new ArgumentException("Código de moeda inválido.", nameof(value));
        return value;
    }
}
