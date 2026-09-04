using Prumo.Domain.Enums;

namespace Prumo.Domain.Entities;

public sealed class AssetGeographicExposure
{
    private AssetGeographicExposure() { }

    public AssetGeographicExposure(Guid assetId, string countryCode, decimal? percentage, ExposureSource source = ExposureSource.Unknown, string? sourceReference = null, DateTimeOffset? observedAt = null, bool isDerived = false)
    {
        Id = Guid.NewGuid();
        AssetId = assetId;
        CountryCode = ValidateCode(countryCode, 2, nameof(countryCode));
        Percentage = ValidatePercentage(percentage);
        Source = source;
        SourceReference = string.IsNullOrWhiteSpace(sourceReference) ? null : sourceReference.Trim();
        ObservedAt = observedAt?.ToUniversalTime();
        IsDerived = isDerived;
    }

    public Guid Id { get; private set; }
    public Guid AssetId { get; private set; }
    public Asset? Asset { get; private set; }
    public string CountryCode { get; private set; } = string.Empty;
    public decimal? Percentage { get; private set; }
    public ExposureSource Source { get; private set; }
    public string? SourceReference { get; private set; }
    public DateTimeOffset? ObservedAt { get; private set; }
    public bool IsDerived { get; private set; }

    private static string ValidateCode(string value, int length, string name)
    {
        value = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (value.Length != length || value.Any(character => character is < 'A' or > 'Z')) throw new ArgumentException("Código de país inválido.", name);
        return value;
    }

    private static decimal? ValidatePercentage(decimal? value)
    {
        if (value is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(value));
        return value;
    }
}
