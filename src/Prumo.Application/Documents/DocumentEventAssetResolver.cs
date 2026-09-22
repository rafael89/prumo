using System.Text.RegularExpressions;
using Prumo.Domain.Entities;

namespace Prumo.Application.Documents;

/// <summary>Recovers explicit asset references that an AI event may omit.</summary>
public static partial class DocumentEventAssetResolver
{
    public static IReadOnlyList<string> MergeExplicitTickers(
        IEnumerable<string> currentTickers,
        string eventSummary,
        IEnumerable<string> facts,
        IEnumerable<Asset> knownAssets)
    {
        var text = string.Join(' ', new[] { eventSummary }.Concat(facts ?? Array.Empty<string>()));
        var normalized = currentTickers
            .Concat(ExtractTickerLikeTokens(text))
            .Select(value => value.Trim().ToUpperInvariant())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in knownAssets)
        {
            var ticker = asset.Ticker.Trim().ToUpperInvariant();
            var root = TickerRootRegex().Match(ticker).Groups["root"].Value;
            if (normalized.Contains(ticker) || (root.Length >= 4 && ContainsToken(text, root)))
                normalized.Add(ticker);
        }

        return normalized.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool ContainsToken(string text, string token)
        => Regex.IsMatch(text, $@"(?<![A-Z0-9]){Regex.Escape(token)}(?![A-Z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IEnumerable<string> ExtractTickerLikeTokens(string text)
        => TickerRegex().Matches(text).Select(match => match.Value);

    [GeneratedRegex(@"\b[A-Z]{4,6}\d{2}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TickerRegex();

    [GeneratedRegex(@"^(?<root>[A-Z]{4,6})\d{2}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TickerRootRegex();
}
