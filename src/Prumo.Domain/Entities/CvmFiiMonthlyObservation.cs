namespace Prumo.Domain.Entities;

/// <summary>
/// Normalized row from the official CVM FII monthly structured dataset.
/// Nullable values mean that the field was absent or blank in the source;
/// they are never materialized as zero.
/// </summary>
public sealed class CvmFiiMonthlyObservation
{
    private CvmFiiMonthlyObservation() { }

    public CvmFiiMonthlyObservation(
        Guid assetId,
        string fundClassCnpj,
        DateOnly referenceDate,
        int sourceVersion,
        string fingerprint,
        string sourceReference,
        string schemaVersion,
        DateTimeOffset observedAt,
        decimal? netAssetValue,
        decimal? navPerShare,
        decimal? cash)
    {
        if (assetId == Guid.Empty) throw new ArgumentException("O ativo é obrigatório.", nameof(assetId));
        if (string.IsNullOrWhiteSpace(fundClassCnpj)) throw new ArgumentException("O CNPJ fundo/classe é obrigatório.", nameof(fundClassCnpj));
        if (string.IsNullOrWhiteSpace(fingerprint)) throw new ArgumentException("O fingerprint é obrigatório.", nameof(fingerprint));
        if (string.IsNullOrWhiteSpace(sourceReference)) throw new ArgumentException("A referência da fonte é obrigatória.", nameof(sourceReference));
        if (string.IsNullOrWhiteSpace(schemaVersion)) throw new ArgumentException("A versão do schema é obrigatória.", nameof(schemaVersion));

        Id = Guid.NewGuid();
        AssetId = assetId;
        FundClassCnpj = NormalizeCnpj(fundClassCnpj);
        ReferenceDate = referenceDate;
        SourceVersion = sourceVersion;
        Fingerprint = fingerprint.Trim().ToLowerInvariant();
        SourceReference = sourceReference.Trim();
        SchemaVersion = schemaVersion.Trim();
        ObservedAt = observedAt.ToUniversalTime();
        NetAssetValue = netAssetValue;
        NavPerShare = navPerShare;
        Cash = cash;
    }

    public Guid Id { get; private set; }
    public Guid AssetId { get; private set; }
    public string FundClassCnpj { get; private set; } = string.Empty;
    public DateOnly ReferenceDate { get; private set; }
    public int SourceVersion { get; private set; }
    public string Fingerprint { get; private set; } = string.Empty;
    public string SourceReference { get; private set; } = string.Empty;
    public string SchemaVersion { get; private set; } = string.Empty;
    public DateTimeOffset ObservedAt { get; private set; }
    public decimal? NetAssetValue { get; private set; }
    public decimal? NavPerShare { get; private set; }
    public decimal? Cash { get; private set; }
    public Asset? Asset { get; private set; }

    private static string NormalizeCnpj(string value) => new(value.Where(char.IsDigit).ToArray());
}
