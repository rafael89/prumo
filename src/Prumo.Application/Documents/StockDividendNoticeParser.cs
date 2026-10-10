using System.Globalization;
using System.Text.RegularExpressions;

namespace Prumo.Application.Documents;

public enum StockDistributionType { Dividend, Jcp }
public enum StockDistributionAmountBasis { Unspecified, Gross, Net }

public sealed record StockDividendNotice(decimal AmountPerShare, DateOnly PaymentDate,
    StockDistributionType Type = StockDistributionType.Dividend,
    StockDistributionAmountBasis AmountBasis = StockDistributionAmountBasis.Unspecified,
    decimal? GrossAmountPerShare = null, DateOnly? BaseDate = null);

public static partial class StockDividendNoticeParser
{
    private const string Amount = @"(?:\d{1,4}(?:\.\d{3})*(?:,\d{1,12})?|\d+(?:\.\d{1,12})?)";
    private const string Date = @"(?:\d{1,2}[./-]\d{1,2}[./-]\d{4})";

    [GeneratedRegex(@"R\$\s*(?<value>" + Amount + @")\s*(?:por|/)\s*aç(?:ão|ao)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PerShareAfterAmount();

    [GeneratedRegex(@"(?:valor\s+(?:estimado\s+)?por\s+aç(?:ão|ao)|dividendo\s+por\s+aç(?:ão|ao))\s*(?:[:=]|é|equivale\s+a|corresponde\s+a)?\s*R\$\s*(?<value>" + Amount + @")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PerShareBeforeAmount();

    [GeneratedRegex(@"(?:data\s+d[eo]\s+pagamento|pagamento\s+(?:dos\s+dividendos\s+)?(?:será\s+realizado|previsto|programado)?(?:\s+para\s+ocorrer)?)\s*(?::|em|para|no\s+dia)?\s*(?<date>" + Date + @")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PaymentDatePattern();

    [GeneratedRegex(@"(?:dividendo\s+por\s+aç(?:ão|ao)|valor\s+por\s+aç(?:ão|ao))[\s\S]{0,120}?data\s+d[eo]\s+pagamento[\s\S]{0,100}?R\$\s*(?<value>" + Amount + @")\s*(?<date>" + Date + @")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DividendTablePattern();

    [GeneratedRegex(@"\b(?:dividendos?|JCP|juros\s+sobre\s+(?:o\s+)?capital\s+próprio)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DistributionTypePattern();

    [GeneratedRegex(@"valor\s+(?<basis>bruto|líquido|liquido)\s+por\s+aç(?:ão|ao)\s*(?:[:=]|é)?\s*R\$\s*(?<value>" + Amount + @")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QualifiedAmountPattern();

    [GeneratedRegex(@"data[-\s]*base\s*:?\s*(?<date>" + Date + @")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BaseDatePattern();

    public static IReadOnlyList<StockDividendNotice> ParseAll(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var markers = DistributionTypePattern().Matches(text).ToArray();
        if (markers.Length == 0) return [];
        var types = markers.Select(x => ReadType(x.Value)).Distinct().ToArray();
        // Repeated labels and table headings for a single type belong to one notice.
        if (types.Length == 1) return ReadDistribution(text, types[0], text);
        var result = new List<StockDividendNotice>();
        var start = 0;
        var type = ReadType(markers[0].Value);
        foreach (var marker in markers.Skip(1))
        {
            var nextType = ReadType(marker.Value);
            if (nextType == type) continue;
            result.AddRange(ReadDistribution(text[start..marker.Index], type, text));
            start = marker.Index;
            type = nextType;
        }
        result.AddRange(ReadDistribution(text[start..], type, text));
        return result.Distinct().ToArray();
    }

    private static StockDistributionType ReadType(string label)
        => label.StartsWith("dividend", StringComparison.OrdinalIgnoreCase)
            ? StockDistributionType.Dividend : StockDistributionType.Jcp;

    private static IReadOnlyList<StockDividendNotice> ReadDistribution(string section, StockDistributionType type, string fullText)
    {
        var table = DividendTablePattern().Matches(section);
        var dates = ReadDates(PaymentDatePattern().Matches(section).Concat(table));
        if (dates.Length == 0) dates = ReadDates(PaymentDatePattern().Matches(fullText).Concat(DividendTablePattern().Matches(fullText)));
        if (dates.Length != 1) return [];
        var qualified = QualifiedAmountPattern().Matches(section).ToArray();
        var net = qualified.Where(x => !x.Groups["basis"].Value.Equals("bruto", StringComparison.OrdinalIgnoreCase))
            .Select(x => ParseAmount(x.Groups["value"].Value)).Where(x => x.HasValue).Distinct().ToArray();
        var gross = qualified.Where(x => x.Groups["basis"].Value.Equals("bruto", StringComparison.OrdinalIgnoreCase))
            .Select(x => ParseAmount(x.Groups["value"].Value)).Where(x => x.HasValue).Distinct().ToArray();
        if (net.Length > 1 || gross.Length > 1) return [];
        var amounts = PerShareAfterAmount().Matches(section).Concat(PerShareBeforeAmount().Matches(section)).Concat(table)
            .Select(x => ParseAmount(x.Groups["value"].Value)).Where(x => x.HasValue).Distinct().ToArray();
        var amount = net.Length == 1 ? net[0] : gross.Length == 1 ? gross[0] : amounts.Length == 1 ? amounts[0] : null;
        if (amount is null) return [];
        var basis = net.Length == 1 ? StockDistributionAmountBasis.Net
            : gross.Length == 1 || section.Contains("bruto", StringComparison.OrdinalIgnoreCase) ? StockDistributionAmountBasis.Gross
            : section.Contains("líquido", StringComparison.OrdinalIgnoreCase) || section.Contains("liquido", StringComparison.OrdinalIgnoreCase)
                ? StockDistributionAmountBasis.Net : StockDistributionAmountBasis.Unspecified;
        var bases = ReadDates(BaseDatePattern().Matches(section));
        if (bases.Length == 0) bases = ReadDates(BaseDatePattern().Matches(fullText));
        if (bases.Length > 1) return [];
        return [new StockDividendNotice(amount.Value, dates[0], type, basis,
            gross.Length == 1 ? gross[0] : basis == StockDistributionAmountBasis.Gross ? amount : null,
            bases.Length == 1 ? bases[0] : null)];
    }

    private static DateOnly[] ReadDates(IEnumerable<Match> matches)
        => matches.Select(x => ParseDate(x.Groups["date"].Value)).Where(x => x.HasValue)
            .Select(x => x!.Value).Distinct().ToArray();

    public static StockDividendNotice? Parse(string text)
    {
        if (!text.Contains("dividend", StringComparison.OrdinalIgnoreCase)) return null;
        var table = DividendTablePattern().Matches(text);
        var amounts = PerShareAfterAmount().Matches(text).Concat(PerShareBeforeAmount().Matches(text)).Concat(table)
            .Select(x => ParseAmount(x.Groups["value"].Value)).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var dates = PaymentDatePattern().Matches(text).Concat(table).Select(x => ParseDate(x.Groups["date"].Value))
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        return amounts.Length == 1 && dates.Length == 1 ? new StockDividendNotice(amounts[0], dates[0]) : null;
    }

    private static decimal? ParseAmount(string value)
    {
        var normalized = value.Contains(',') ? value.Replace(".", "").Replace(',', '.') : value;
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) && amount > 0 && amount <= 1000 ? amount : null;
    }

    private static DateOnly? ParseDate(string value)
        => DateOnly.TryParseExact(value, new[] { "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "d.M.yyyy", "dd.MM.yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
