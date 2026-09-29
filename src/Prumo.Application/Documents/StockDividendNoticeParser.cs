using System.Globalization;
using System.Text.RegularExpressions;

namespace Prumo.Application.Documents;

public sealed record StockDividendNotice(decimal AmountPerShare, DateOnly PaymentDate);

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
