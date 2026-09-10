using Prumo.Domain.Enums;

namespace Prumo.Application.Contributions;

public enum UnderlyingSectorExposureStatus { Available, Unavailable }

public sealed record UnderlyingSectorExposure(
    UnderlyingSectorExposureStatus Status,
    IReadOnlyDictionary<string, decimal>? Sectors = null)
{
    public static UnderlyingSectorExposure Unavailable { get; } = new(UnderlyingSectorExposureStatus.Unavailable);
}

public static class SectorExposureSemantics
{
    public static FundType InferFundType(AssetType assetType, string? sector) =>
        assetType == AssetType.Fii && string.Equals(sector?.Trim(), "Fund of Funds", StringComparison.OrdinalIgnoreCase)
            ? FundType.FundOfFunds
            : assetType == AssetType.Fii && sector?.Contains("Receivables", StringComparison.OrdinalIgnoreCase) == true
                ? FundType.Receivables
                : assetType == AssetType.Fii ? FundType.Brick : FundType.Unknown;

    public static bool IsFundOfFunds(FundType fundType, string? sector) =>
        fundType == FundType.FundOfFunds || string.Equals(sector?.Trim(), "Fund of Funds", StringComparison.OrdinalIgnoreCase);
}
