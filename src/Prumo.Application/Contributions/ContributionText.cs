using System.Globalization;
using System.Text.RegularExpressions;
using Prumo.Domain.Enums;

namespace Prumo.Application.Contributions;

public static class ContributionText
{
    public static string AssetClassLabel(AssetClass assetClass) => assetClass switch
    {
        AssetClass.BrazilianStock => "A\u00e7\u00f5es",
        AssetClass.Fii => "FIIs",
        AssetClass.FixedIncome => "Renda fixa",
        AssetClass.International => "ETF",
        _ => "Outros ativos",
    };

    public static string NormalizeAssetClassNames(string text)
    {
        var normalized = text
        .Replace(nameof(AssetClass.BrazilianStock), AssetClassLabel(AssetClass.BrazilianStock), StringComparison.Ordinal)
        .Replace(nameof(AssetClass.Fii), AssetClassLabel(AssetClass.Fii), StringComparison.Ordinal)
        .Replace(nameof(AssetClass.FixedIncome), AssetClassLabel(AssetClass.FixedIncome), StringComparison.Ordinal)
        .Replace(nameof(AssetClass.International), AssetClassLabel(AssetClass.International), StringComparison.Ordinal)
        .Replace(nameof(AssetClass.Other), AssetClassLabel(AssetClass.Other), StringComparison.Ordinal)
        .Replace("currentClassWeight abaixo de targetClassWeight", "o peso atual da classe está abaixo da meta", StringComparison.OrdinalIgnoreCase)
        .Replace("currentClassWeight", "peso atual da classe", StringComparison.OrdinalIgnoreCase)
        .Replace("targetClassWeight", "meta da classe", StringComparison.OrdinalIgnoreCase)
        .Replace("classBudget", "limite calculado para a classe", StringComparison.OrdinalIgnoreCase)
        .Replace("candidato elegível", "ativo elegível", StringComparison.OrdinalIgnoreCase)
        .Replace("candidatos elegíveis", "ativos elegíveis", StringComparison.OrdinalIgnoreCase);

        normalized = Regex.Replace(normalized, @"aporte total de\s+(?<amount>\d+(?:[.,]\d{1,2})?)", match =>
        {
            var raw = match.Groups["amount"].Value.Replace(',', '.');
            return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
                ? $"aporte total de {amount.ToString("C", CultureInfo.GetCultureInfo("pt-BR"))}"
                : match.Value;
        }, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return normalized;
    }
}
