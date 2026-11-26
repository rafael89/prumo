using System.Globalization;
using System.Text.RegularExpressions;

namespace Prumo.Application.Documents;

public enum StockDistributionType { Dividend, Jcp }
public enum StockDistributionAmountBasis { Unspecified, Gross, Net, EstimatedNet }

public sealed record StockDividendNotice(decimal AmountPerShare, DateOnly PaymentDate,
    StockDistributionType Type = StockDistributionType.Dividend,
    StockDistributionAmountBasis AmountBasis = StockDistributionAmountBasis.Unspecified,
    decimal? GrossAmountPerShare = null, DateOnly? BaseDate = null, bool WithholdingApplies = false);

public static partial class StockDividendNoticeParser
{
    private const string Amount = @"(?:\d{1,4}(?:\.\d{3})*(?:,\s*\d{1,12})?|\d+(?:\.\d{1,12})?)";
    private const string Date = @"(?:\d{4}-\d{2}-\d{2}|\d{1,2}[./-]\d{1,2}[./-]\d{4}|\d{1,2}\s+de\s+[a-zç]+\s+de\s+\d{4})";
    private const string Ticker = @"[A-Z0-9]{4}\d{1,2}";
    private const string DateConnector = @"(?::|em|para|no\s+dia)?";

    [GeneratedRegex(@"R\$\s*(?<value>" + Amount + @")\s*(?:por|/)\s*aç(?:ão|ao)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PerShareAfterAmount();

    [GeneratedRegex(@"(?:valor\s+(?:estimado\s+)?por\s+aç(?:ão|ao)|dividendo\s+por\s+aç(?:ão|ao))\s*(?:[:=]|é|equivale\s+a|corresponde\s+a)?\s*R\$\s*(?<value>" + Amount + @")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PerShareBeforeAmount();

    [GeneratedRegex(@"(?:data\s+d[eo]\s+pagamento|pagamento\s*(?:dos\s+dividendos(?:\s+intercalares)?(?:\s+e\s+JCP)?\s+)?(?:será\s+realizado|previsto|programado|ocorrerá)?(?:\s+para\s+ocorrer)?)\s*" + DateConnector + @"\s*(?<date>" + Date + @")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PaymentDatePattern();

    [GeneratedRegex(@"(?:dividendo\s+por\s+aç(?:ão|ao)|valor\s+por\s+aç(?:ão|ao))[\s\S]{0,120}?data\s+d[eo]\s+pagamento[\s\S]{0,100}?R\$\s*(?<value>" + Amount + @")\s*(?<date>" + Date + @")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DividendTablePattern();

    [GeneratedRegex(@"\b(?:dividendos?|JCP|juros\s+sobre\s+(?:o\s+)?capital\s+próprio)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DistributionTypePattern();

    [GeneratedRegex(@"valor\s+(?<basis>bruto|líquido|liquido)\s+por\s+aç(?:ão|ao)\s*(?:[:=]|é)?\s*R\$\s*(?<value>" + Amount + @")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QualifiedAmountPattern();

    [GeneratedRegex(@"(?:data[-\s]*base(?:\s+será|\s+para\s+[^.;]{1,80}?\s+é)?|posi[çc][ãa]o\s+acion[áa]ria)\s*(?::|em|do\s+dia)?\s*(?<date>" + Date + @")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BaseDatePattern();

    [GeneratedRegex(@"(?<type>dividendos?(?:\s+(?:intercalares|intermediários|adicionais))?|JCP|juros\s+sobre\s+(?:o\s+)?capital\s+próprio)\s+por\s+unit\s+(?<unit>" + Ticker + @")\s*\(R\$\)[\s|]*R\$\s*" + Amount + @"[\s|]+R\$\s*(?<shareAmount>" + Amount + @")[\s|]+R\$\s*(?<unitAmount>" + Amount + @")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnitTableRow();

    [GeneratedRegex(@"por\s+aç(?:ão|ao)\s+(?<tickers>" + Ticker + @"(?:\s*/\s*" + Ticker + @")*)\s*\(R\$\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShareTableColumn();

    [GeneratedRegex(@"(?:por\s+aç(?:ão|ao)\s+(?:ordinária\s+e\s+preferencial|preferencial\s+e\s+ordinária|ON\s+e\s+PN)|por\s+aç(?:ão|ao)\s+(?:ON/PN|PN/ON))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CommonShareAmount();

    [GeneratedRegex(@"\b(?:primeira|segunda|terceira|quarta|[1-9]\s*[ªa])\s+parcela\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstallmentLabel();

    [GeneratedRegex(@"(?:no\s+valor\s+de|de)\s+R\$\s*(?<value>" + Amount + @")\s*(?:por\s+aç(?:ão|ao)[^.;]{0,75}?)?\s*,?\s*(?:será\s+paga|paga|com\s+pagamento)\s+(?:em|no\s+dia)\s+(?<date>" + Date + @")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstallmentPayment();

    [GeneratedRegex(@"(?:R\$\s*)?(?<value>" + Amount + @")\s+(?:sob\s+a\s+forma\s+de|em|a\s+título\s+de|como)\s+(?<type>dividendos?|JCP|juros\s+sobre\s+(?:o\s+)?capital\s+próprio)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstallmentComponent();

    [GeneratedRegex(@"^\s*,?\s*(?:(?:integralmente|inteiramente|100\s*%)\s+(?:sob\s+a\s+forma\s+de|em|a\s+título\s+de|como)|como)\s+(?<type>dividendos?|JCP|juros\s+sobre\s+(?:o\s+)?capital\s+próprio)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FullInstallmentType();

    [GeneratedRegex(@"\b(?:imposto\s+d[eo]\s+renda|IRRF)\s+(?:retido\s+)?na\s+fonte\b|\bsujeit[oa]s?\s+à\s+incidência\s+d[eo]\s+imposto\s+d[eo]\s+renda\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Withholding();

    public static IReadOnlyList<StockDividendNotice> ParseAll(string text, string? ticker = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var table = UnitTableRow().Matches(text);
        if (table.Count > 0) return ReadUnitTable(text, ticker, table);
        var installments = ParseInstallments(text, ticker);
        if (installments.Count > 0) return installments;
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

    private static IReadOnlyList<StockDividendNotice> ReadUnitTable(string text, string? ticker, MatchCollection rows)
    {
        var dates = ReadDates(PaymentDatePattern().Matches(text));
        var bases = ReadDates(BaseDatePattern().Matches(text));
        if (ticker is null || dates.Length != 1 || bases.Length > 1) return [];
        var shareTickers = ShareTableColumn().Matches(text).SelectMany(x => x.Groups["tickers"].Value.Split('/'))
            .Select(x => x.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasWithholding = Withholding().IsMatch(text);
        var notices = new List<StockDividendNotice>();
        foreach (Match row in rows)
        {
            var amountField = row.Groups["unit"].Value.Equals(ticker, StringComparison.OrdinalIgnoreCase) ? "unitAmount"
                : shareTickers.Contains(ticker) ? "shareAmount" : null;
            if (amountField is null || ParseAmount(row.Groups[amountField].Value) is not { } amount) continue;
            var type = ReadType(row.Groups["type"].Value);
            notices.Add(new(amount, dates[0], type, type == StockDistributionType.Jcp ? StockDistributionAmountBasis.Gross : StockDistributionAmountBasis.Unspecified,
                type == StockDistributionType.Jcp ? amount : null, bases.Length == 1 ? bases[0] : null,
                type == StockDistributionType.Jcp && hasWithholding));
        }
        return notices.Distinct().ToArray();
    }

    public static IReadOnlyList<StockDividendNotice> ParseInstallments(string text, string? ticker)
    {
        if (ticker is not { Length: 5 } || ticker[^1] is not ('3' or '4') || !CommonShareAmount().IsMatch(text)) return [];
        var labels = InstallmentLabel().Matches(text);
        var bases = ReadDates(BaseDatePattern().Matches(text));
        if (labels.Count < 2 || bases.Length != 1) return [];
        var hasWithholding = Withholding().IsMatch(text);
        var notices = new List<StockDividendNotice>();
        for (var i = 0; i < labels.Count; i++)
        {
            var end = i + 1 < labels.Count ? labels[i + 1].Index : text.Length;
            var section = text[labels[i].Index..end];
            var payment = InstallmentPayment().Match(section);
            if (!payment.Success || ParseAmount(payment.Groups["value"].Value) is not { } total
                || ParseDate(payment.Groups["date"].Value) is not { } date) continue;
            var terms = section[(payment.Index + payment.Length)..].Split(';', 2)[0];
            var fullType = FullInstallmentType().Match(terms);
            if (fullType.Success)
            {
                notices.Add(Create(total, ReadType(fullType.Groups["type"].Value)));
                continue;
            }
            var components = InstallmentComponent().Matches(terms).Select(x =>
                (Amount: ParseAmount(x.Groups["value"].Value), Type: ReadType(x.Groups["type"].Value))).ToArray();
            if (components.Length > 0 && components.Sum(x => x.Amount) == total && components.Select(x => x.Type).Distinct().Count() == components.Length)
                notices.AddRange(components.Select(x => Create(x.Amount!.Value, x.Type)));

            StockDividendNotice Create(decimal amount, StockDistributionType type)
                => new(amount, date, type, type == StockDistributionType.Jcp ? StockDistributionAmountBasis.Gross : StockDistributionAmountBasis.Unspecified,
                    type == StockDistributionType.Jcp ? amount : null, bases[0], type == StockDistributionType.Jcp && hasWithholding);
        }
        return notices.Distinct().ToArray();
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
        value = string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
        var normalized = value.Contains(',') ? value.Replace(".", "").Replace(',', '.') : value;
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) && amount > 0 && amount <= 1000 ? amount : null;
    }

    private static DateOnly? ParseDate(string value)
        => DateOnly.TryParseExact(string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)),
            new[] { "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "d.M.yyyy", "dd.MM.yyyy", "yyyy-MM-dd", "d 'de' MMMM 'de' yyyy", "dd 'de' MMMM 'de' yyyy" },
            CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.None, out var date) ? date : null;
}
