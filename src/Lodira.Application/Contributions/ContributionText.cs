using Lodira.Domain.Enums;

namespace Lodira.Application.Contributions;

public static class ContributionText
{
    public static string AssetClassLabel(AssetClass assetClass) => assetClass switch
    {
        AssetClass.BrazilianStock => "A\u00e7\u00f5es",
        AssetClass.Fii => "FIIs",
        AssetClass.FixedIncome => "Renda fixa",
        AssetClass.International => "Ativos internacionais",
        _ => "Outros ativos",
    };

    public static string NormalizeAssetClassNames(string text) => text
        .Replace(nameof(AssetClass.BrazilianStock), AssetClassLabel(AssetClass.BrazilianStock), StringComparison.Ordinal)
        .Replace(nameof(AssetClass.Fii), AssetClassLabel(AssetClass.Fii), StringComparison.Ordinal)
        .Replace(nameof(AssetClass.FixedIncome), AssetClassLabel(AssetClass.FixedIncome), StringComparison.Ordinal)
        .Replace(nameof(AssetClass.International), AssetClassLabel(AssetClass.International), StringComparison.Ordinal)
        .Replace(nameof(AssetClass.Other), AssetClassLabel(AssetClass.Other), StringComparison.Ordinal);
}
