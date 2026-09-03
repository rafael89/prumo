using Prumo.Domain.Enums;

namespace Prumo.Domain.Entities;

public sealed class Asset
{
    private Asset()
    {
    }

    public Asset(string ticker, string name, AssetType assetType, string? sector, AssetClass? assetClass = null, string? cnpj = null)
    {
        Id = Guid.NewGuid();
        Update(ticker, name, assetType, sector);
        IsActive = true;
        AssetClass = assetClass ?? DeriveAssetClass(assetType);
        Cnpj = NormalizeCnpj(cnpj);
    }

    public Guid Id { get; private set; }
    public string Ticker { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public AssetType AssetType { get; private set; }
    public AssetClass AssetClass { get; private set; }
    public string? Sector { get; private set; }
    public string? Cnpj { get; private set; }
    public bool IsActive { get; private set; }

    public void SetCnpj(string? cnpj) => Cnpj = NormalizeCnpj(cnpj);

    public void Update(string ticker, string name, AssetType assetType, string? sector)
    {
        Ticker = ValidateText(ticker, nameof(ticker)).ToUpperInvariant();
        Name = ValidateText(name, nameof(name));
        AssetType = assetType;
        AssetClass = DeriveAssetClass(assetType);
        Sector = string.IsNullOrWhiteSpace(sector) ? null : sector.Trim();
    }

    private static string ValidateText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("O valor é obrigatório.", parameterName);
        }

        return value.Trim();
    }

    private static AssetClass DeriveAssetClass(AssetType assetType) => assetType switch
    {
        AssetType.Stock => AssetClass.BrazilianStock,
        AssetType.Fii => AssetClass.Fii,
        _ => AssetClass.Other
    };

    private static string? NormalizeCnpj(string? cnpj)
    {
        var digits = new string((cnpj ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length == 14 ? digits : null;
    }
}
