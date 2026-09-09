using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Infrastructure.MarketData;

/// <summary>
/// Small, curated starting catalog for structural facts used by contribution
/// intelligence. It deliberately omits economic weights unless they have a
/// stable, reviewable interpretation.
/// </summary>
public static class AssetMetadataCatalog
{
    public const string Version = "r5-asset-metadata-v1";
    private const string Reference = "Prumo curated asset metadata catalog r5-asset-metadata-v1";
    private static readonly DateTimeOffset ReferenceDate = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);

    private sealed record Entry(
        string? Sector,
        EconomicSector? SectorCategory,
        string? ListingCountryCode,
        string? TradingCurrency,
        IReadOnlyList<(string Code, decimal Weight)>? GeographicExposures = null,
        IReadOnlyList<(string Code, decimal Weight)>? CurrencyExposures = null);

    private static readonly IReadOnlyDictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase)
    {
        ["PETR4"] = new("Energy", EconomicSector.Energy, "BR", "BRL"),
        ["ITSA4"] = new("Financials", EconomicSector.Financials, "BR", "BRL"),
        ["ITUB4"] = new("Financials", EconomicSector.Financials, "BR", "BRL"),
        ["BBDC3"] = new("Financials", EconomicSector.Financials, "BR", "BRL"),
        ["BBDC4"] = new("Financials", EconomicSector.Financials, "BR", "BRL"),
        ["BBSE3"] = new("Financials", EconomicSector.Financials, "BR", "BRL"),
        ["EGIE3"] = new("Utilities", EconomicSector.Utilities, "BR", "BRL"),
        ["TAEE11"] = new("Utilities", EconomicSector.Utilities, "BR", "BRL"),
        ["WEGE3"] = new("Industrials", EconomicSector.Industrials, "BR", "BRL"),
        ["VALE3"] = new("Materials", EconomicSector.Materials, "BR", "BRL"),
        // Current portfolio FIIs. The classification is intentionally at the
        // fund strategy level; it does not imply a fabricated country/currency
        // split for the underlying receivables or properties.
        ["AFHI11"] = new("Real Estate Receivables", EconomicSector.RealEstate, "BR", "BRL"),
        ["BTLG11"] = new("Logistics Real Estate", EconomicSector.RealEstate, "BR", "BRL"),
        ["CPTS11"] = new("Real Estate Receivables", EconomicSector.RealEstate, "BR", "BRL"),
        ["FIIB11"] = new("Industrial Real Estate", EconomicSector.RealEstate, "BR", "BRL"),
        ["GGRC11"] = new("Logistics Real Estate", EconomicSector.RealEstate, "BR", "BRL"),
        ["HFOF11"] = new("Fund of Funds", EconomicSector.RealEstate, "BR", "BRL"),
        ["HGBS11"] = new("Shopping Centers", EconomicSector.RealEstate, "BR", "BRL"),
        ["HGLG11"] = new("Logistics Real Estate", EconomicSector.RealEstate, "BR", "BRL"),
        ["HGRU11"] = new("Urban Retail Real Estate", EconomicSector.RealEstate, "BR", "BRL"),
        ["LVBI11"] = new("Logistics Real Estate", EconomicSector.RealEstate, "BR", "BRL"),
        ["TRXF11"] = new("Urban Retail Real Estate", EconomicSector.RealEstate, "BR", "BRL"),
        ["VGIP11"] = new("Real Estate Receivables", EconomicSector.RealEstate, "BR", "BRL"),
        ["VILG11"] = new("Logistics Real Estate", EconomicSector.RealEstate, "BR", "BRL"),
        ["VRTA11"] = new("Real Estate Receivables", EconomicSector.RealEstate, "BR", "BRL"),
        ["XPIN11"] = new("Industrial Real Estate", EconomicSector.RealEstate, "BR", "BRL"),
        ["XPLG11"] = new("Logistics Real Estate", EconomicSector.RealEstate, "BR", "BRL"),
        ["XPML11"] = new("Shopping Centers", EconomicSector.RealEstate, "BR", "BRL"),
        // IVVB11 has a clear economic interpretation. Other broad/global ETFs
        // remain unavailable until the catalog has a reviewed methodology.
        ["IVVB11"] = new("Broad market ETF", null, "BR", "BRL", new[] { ("US", 100m) }, new[] { ("USD", 100m) })
    };

    public static bool Apply(Asset asset)
    {
        if (!Entries.TryGetValue(asset.Ticker, out var entry))
        {
            entry = asset.AssetType == AssetType.Fii
                ? new Entry("Real Estate", EconomicSector.RealEstate, "BR", "BRL")
                : asset.AssetClass == AssetClass.BrazilianStock
                    ? new Entry(null, null, "BR", "BRL")
                    : asset.AssetClass == AssetClass.FixedIncome
                        ? new Entry(null, null, "BR", "BRL")
                    : null;
        }
        if (entry is null) return false;
        return asset.ApplyCuratedMetadata(
            entry.Sector,
            entry.SectorCategory,
            entry.ListingCountryCode,
            entry.TradingCurrency,
            entry.GeographicExposures,
            entry.CurrencyExposures,
            Reference,
            ReferenceDate);
    }
}

public sealed class AssetMetadataCatalogSyncService(PrumoDbContext db)
{
    public async Task<int> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var assets = await db.Assets
            .Include(asset => asset.GeographicExposures)
            .Include(asset => asset.CurrencyExposures)
            .ToListAsync(cancellationToken);
        var changed = assets.Count(AssetMetadataCatalog.Apply);
        if (changed > 0) await db.SaveChangesAsync(cancellationToken);
        return changed;
    }
}
