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
    public EconomicSector? SectorCategory { get; private set; }
    public string? ListingCountryCode { get; private set; }
    public string? TradingCurrency { get; private set; }
    public ExposureCoverage GeographicExposureCoverage { get; private set; } = ExposureCoverage.Unavailable;
    public ExposureCoverage EconomicCurrencyExposureCoverage { get; private set; } = ExposureCoverage.Unavailable;
    public string? Cnpj { get; private set; }
    public bool IsActive { get; private set; }
    public ICollection<AssetGeographicExposure> GeographicExposures { get; private set; } = new List<AssetGeographicExposure>();
    public ICollection<AssetCurrencyExposure> CurrencyExposures { get; private set; } = new List<AssetCurrencyExposure>();
    public ICollection<AssetFundamentalSnapshot> FundamentalSnapshots { get; private set; } = new List<AssetFundamentalSnapshot>();
    public ICollection<AssetValuationSnapshot> ValuationSnapshots { get; private set; } = new List<AssetValuationSnapshot>();
    public ICollection<DocumentFinancialFact> DocumentFinancialFacts { get; private set; } = new List<DocumentFinancialFact>();
    public ICollection<CvmFiiMonthlyObservation> CvmFiiMonthlyObservations { get; private set; } = new List<CvmFiiMonthlyObservation>();

    public void SetCnpj(string? cnpj) => Cnpj = NormalizeCnpj(cnpj);
    public void SetListingMetadata(string? countryCode, string? currency)
    {
        ListingCountryCode = string.IsNullOrWhiteSpace(countryCode) ? null : countryCode.Trim().ToUpperInvariant();
        TradingCurrency = string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant();
    }

    /// <summary>Applies missing facts from a versioned curated catalog without replacing richer existing data.</summary>
    public bool ApplyCuratedMetadata(
        string? sector,
        EconomicSector? sectorCategory,
        string? listingCountryCode,
        string? tradingCurrency,
        IReadOnlyList<(string Code, decimal Weight)>? geographicExposures,
        IReadOnlyList<(string Code, decimal Weight)>? currencyExposures,
        string sourceReference,
        DateTimeOffset observedAt)
    {
        var changed = false;
        if (string.IsNullOrWhiteSpace(Sector) && !string.IsNullOrWhiteSpace(sector))
        {
            Sector = sector.Trim();
            changed = true;
        }
        if (SectorCategory is null && sectorCategory.HasValue)
        {
            SectorCategory = sectorCategory;
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(ListingCountryCode) && !string.IsNullOrWhiteSpace(listingCountryCode))
        {
            ListingCountryCode = listingCountryCode.Trim().ToUpperInvariant();
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(TradingCurrency) && !string.IsNullOrWhiteSpace(tradingCurrency))
        {
            TradingCurrency = tradingCurrency.Trim().ToUpperInvariant();
            changed = true;
        }
        if (GeographicExposureCoverage == ExposureCoverage.Unavailable && GeographicExposures.Count == 0 && geographicExposures is { Count: > 0 })
        {
            foreach (var exposure in geographicExposures)
                GeographicExposures.Add(new AssetGeographicExposure(Id, exposure.Code, exposure.Weight, ExposureSource.Manual, sourceReference, observedAt));
            GeographicExposureCoverage = geographicExposures.Sum(x => x.Weight) == 100m ? ExposureCoverage.Complete : ExposureCoverage.Partial;
            changed = true;
        }
        if (EconomicCurrencyExposureCoverage == ExposureCoverage.Unavailable && CurrencyExposures.Count == 0 && currencyExposures is { Count: > 0 })
        {
            foreach (var exposure in currencyExposures)
                CurrencyExposures.Add(new AssetCurrencyExposure(Id, exposure.Code, exposure.Weight, ExposureSource.Manual, sourceReference, observedAt));
            EconomicCurrencyExposureCoverage = currencyExposures.Sum(x => x.Weight) == 100m ? ExposureCoverage.Complete : ExposureCoverage.Partial;
            changed = true;
        }
        return changed;
    }

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
        AssetType.Etf => AssetClass.International,
        _ => AssetClass.Other
    };

    private static string? NormalizeCnpj(string? cnpj)
    {
        var digits = new string((cnpj ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length == 14 ? digits : null;
    }
}
