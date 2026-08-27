using Prumo.Domain.Enums;

namespace Prumo.Domain.Entities;

public sealed class Asset
{
    private Asset()
    {
    }

    public Asset(string ticker, string name, AssetType assetType, string? sector)
    {
        Id = Guid.NewGuid();
        Update(ticker, name, assetType, sector);
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public string Ticker { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public AssetType AssetType { get; private set; }
    public string? Sector { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string ticker, string name, AssetType assetType, string? sector)
    {
        Ticker = ValidateText(ticker, nameof(ticker)).ToUpperInvariant();
        Name = ValidateText(name, nameof(name));
        AssetType = assetType;
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
}
