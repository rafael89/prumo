using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Prumo.Domain.Enums;

namespace Prumo.Application.Radar;

public sealed record RadarCardSource(Guid AnalysisId, Guid AssetId, AssetType AssetType,
    DateTimeOffset PublishedAt, InvestmentEventType EventType, string Summary);

/// <summary>Presentation grouping only: the original documents and events remain intact.</summary>
public static class RadarCardGrouping
{
    private static readonly HashSet<string> StopWords = new(
        "para pela pelo pelas pelos sobre apos entre referente referentes companhia empresa sociedade divulgado divulgacao publicacao documento documentos geral extraordinaria".Split(' '));

    public static IReadOnlyDictionary<Guid, Guid> Build(IEnumerable<RadarCardSource> sources)
    {
        var result = new Dictionary<Guid, Guid>();
        foreach (var issuer in sources.GroupBy(x => x.AssetId))
        {
            var cards = new List<List<RadarCardSource[]>>();
            foreach (var document in issuer.GroupBy(x => x.AnalysisId)
                .OrderBy(x => x.Min(s => s.PublishedAt)).ThenBy(x => x.Key))
            {
                var items = document.ToArray();
                var card = cards.FirstOrDefault(group => items[0].AssetType == AssetType.Stock
                    && items[0].PublishedAt - group[0][0].PublishedAt <= TimeSpan.FromDays(7)
                    // Require a match with every document; do not merge unrelated stories through a bridge.
                    && group.All(other => items.Any(a => other.Any(b => Similar(a, b)))));
                if (card is null) { card = []; cards.Add(card); }
                card.Add(items);
                result[document.Key] = card[0][0].AnalysisId;
            }
        }
        return result;
    }

    private static bool Similar(RadarCardSource a, RadarCardSource b)
    {
        // Recurring cash payments and capital changes need explicit identity, not textual similarity.
        if (a.EventType is InvestmentEventType.Distribution or InvestmentEventType.CapitalRaise or InvestmentEventType.Liquidation
            || b.EventType is InvestmentEventType.Distribution or InvestmentEventType.CapitalRaise or InvestmentEventType.Liquidation)
            return false;
        if (a.EventType != b.EventType && !(Corporate(a.EventType) && Corporate(b.EventType))) return false;
        var left = Tokens(a.Summary);
        var right = Tokens(b.Summary);
        // Results for different periods or with revised figures must remain separate.
        var leftNumbers = left.Where(x => x.Any(char.IsDigit)).ToHashSet();
        var rightNumbers = right.Where(x => x.Any(char.IsDigit)).ToHashSet();
        if (a.EventType == InvestmentEventType.FinancialResult && leftNumbers.Count > 0 && rightNumbers.Count > 0
            && !leftNumbers.SetEquals(rightNumbers)) return false;
        var leftAmounts = Regex.Matches(a.Summary, @"R\$\s*[\d.,]+").Select(x => x.Value.Replace(" ", "")).ToHashSet();
        var rightAmounts = Regex.Matches(b.Summary, @"R\$\s*[\d.,]+").Select(x => x.Value.Replace(" ", "")).ToHashSet();
        if (leftAmounts.Count > 0 && rightAmounts.Count > 0 && !leftAmounts.SetEquals(rightAmounts)) return false;
        // An incorporation produces distinct acts (appraisal, board opinion, meeting call).
        // The named subsidiary is a stronger subject anchor than similarity of those acts.
        if (Corporate(a.EventType) && Corporate(b.EventType) && left.Contains("incorporacao") && right.Contains("incorporacao"))
        {
            var leftSubjects = Subsidiaries(a.Summary).ToHashSet();
            var rightSubjects = Subsidiaries(b.Summary).ToHashSet();
            if (leftSubjects.Count > 0 && rightSubjects.Count > 0)
                return leftSubjects.SetEquals(rightSubjects);
        }
        var common = left.Intersect(right).Count();
        return common >= 6 && common >= Math.Min(left.Count, right.Count) * .65
            && common >= left.Union(right).Count() * .4;
    }

    private static bool Corporate(InvestmentEventType type)
        => type is InvestmentEventType.CorporateAction or InvestmentEventType.AssetTransaction or InvestmentEventType.Governance;

    private static IEnumerable<string> Subsidiaries(string summary)
        => Regex.Matches(summary, @"\b(?:\p{Lu}[\p{L}\d]*[ .]+){2,10}(?i:Ltda)\b")
            .Select(x => string.Join(' ', Tokens(x.Value).Order()));

    private static HashSet<string> Tokens(string text)
    {
        var normalized = new string(text.ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return Regex.Matches(normalized, @"[a-z0-9]+(?:[.,/%][0-9]+)*")
            .Select(x => x.Value).Where(x => (x.Length > 2 || x.Any(char.IsDigit)) && !StopWords.Contains(x)).ToHashSet();
    }
}
