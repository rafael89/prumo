using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Prumo.Application.Documents;

public sealed record FiiDistributionNotice(decimal AmountPerUnit, DateOnly PaymentDate, DateOnly? BaseDate);

public static partial class FiiDistributionNoticeParser
{
    [GeneratedRegex(@"(?im)(?:C[oó]digo\s+de\s+negocia[cç][aã]o|Cod(?:igo)?Negociacao|CodigoAtivo)\s*(?::\s*\|?|\|)\s*(?<ticker>[A-Z0-9]{4}\d{1,2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex TickerField();

    [GeneratedRegex(@"(?im)^\s*(?:Valor\s+do\s+provento\s*(?:\(R\$\s*/\s*unidade\))?|ValorProvento(?:PorCota|PorUnidade)?|ValorRendimento(?:PorCota|PorUnidade)?)\s*(?:[:|]\s*|\s+)(?:R\$\s*)?(?<value>\d[\d.,]*)", RegexOptions.CultureInvariant)]
    private static partial Regex AmountField();

    [GeneratedRegex(@"(?im)^\s*(?:Data\s+do\s+pagamento|DataPagamento|DtPagamento)\s*(?:[:|]\s*|\s+)(?<value>\d{1,2}[/-]\d{1,2}[/-]\d{4}|\d{4}-\d{2}-\d{2})", RegexOptions.CultureInvariant)]
    private static partial Regex PaymentField();

    [GeneratedRegex(@"(?im)^\s*(?:Data-base(?:\s*\([^\r\n]*?\))?|DataBase|DtBase)\s*(?:[:|]\s*|\s+)(?<value>\d{1,2}[/-]\d{1,2}[/-]\d{4}|\d{4}-\d{2}-\d{2})", RegexOptions.CultureInvariant)]
    private static partial Regex BaseField();

    [GeneratedRegex(@"(?<![\d.,])(?:R\$\s*)?(?<value>\d{1,4}(?:[.,]\d{1,12})?)\s*(?:por|/)\s*(?:cota|unidade)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NarrativeAmount();

    [GeneratedRegex(@"\b(?:data\s+(?:de|do)\s+pagamento|pagamento)(?:\s+(?:do\s+rendimento|ser[aá]\s+realizado|previsto|programado))?\s*(?::|=|[eé](?:\s+de)?|em|para|no\s+dia)?\s*(?<value>\d{4}-\d{2}-\d{2}|\d{1,2}[./-]\d{1,2}[./-]\d{4}|\d{1,2}\s+de\s+[a-zç]+\s+de\s+\d{4})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NarrativePayment();

    [GeneratedRegex(@"\b(?:data[-\s]*base|com\s+base)\s*(?:em|[eé]|:)?\s*(?<value>\d{4}-\d{2}-\d{2}|\d{1,2}[./-]\d{1,2}[./-]\d{4}|\d{1,2}\s+de\s+[a-zç]+\s+de\s+\d{4})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NarrativeBase();

    public static bool IsNotice(string? category, string? type)
        => Normalize(category).Contains("AVISO AOS COTISTAS", StringComparison.Ordinal)
            && Normalize(type).Contains("RENDIMENTOS E AMORTIZACOES", StringComparison.Ordinal);

    public static FiiDistributionNotice? Parse(string text, string? ticker = null, string? analysisSummary = null)
    {
        var tickers = TickerField().Matches(text);
        if (ticker is null || tickers.Count == 0)
            return ParseFields(text) ?? (analysisSummary is null ? null : ParseAnalysisSummary(analysisSummary));

        var notices = new List<FiiDistributionNotice>();
        for (var i = 0; i < tickers.Count; i++)
        {
            if (!tickers[i].Groups["ticker"].Value.Equals(ticker, StringComparison.OrdinalIgnoreCase)) continue;
            var end = i + 1 < tickers.Count ? tickers[i + 1].Index : text.Length;
            var notice = ParseFields(text[tickers[i].Index..end]);
            if (notice is not null) notices.Add(notice);
        }
        return notices.Distinct().Take(2).ToArray() is [var single] ? single : null;
    }

    private static FiiDistributionNotice? ParseFields(string text)
    {
        var amounts = AmountField().Matches(text).Select(x => ParseAmount(x.Groups["value"].Value)).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var payments = PaymentField().Matches(text).Select(x => ParseDate(x.Groups["value"].Value)).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        if (amounts.Length != 1 || payments.Length != 1) return null;
        var bases = BaseField().Matches(text).Select(x => ParseDate(x.Groups["value"].Value)).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        return new FiiDistributionNotice(amounts[0], payments[0], bases.Length == 1 ? bases[0] : null);
    }

    public static FiiDistributionNotice? ParseAnalysisSummary(string summary)
    {
        var amounts = NarrativeAmount().Matches(summary).Select(x => ParseAmount(x.Groups["value"].Value))
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var payments = NarrativePayment().Matches(summary).Select(x => ParseNarrativeDate(x.Groups["value"].Value))
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var bases = NarrativeBase().Matches(summary).Select(x => ParseNarrativeDate(x.Groups["value"].Value))
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        return amounts.Length == 1 && payments.Length == 1
            ? new FiiDistributionNotice(amounts[0], payments[0], bases.Length == 1 ? bases[0] : null)
            : null;
    }

    private static decimal? ParseAmount(string value)
    {
        var decimalSeparator = value.LastIndexOf(',') > value.LastIndexOf('.') ? ',' : '.';
        var normalized = decimalSeparator == ',' ? value.Replace(".", "").Replace(',', '.') : value.Replace(",", "");
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) && amount > 0 && amount <= 1000 ? amount : null;
    }

    private static DateOnly? ParseDate(string value)
        => DateOnly.TryParseExact(value, new[] { "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static DateOnly? ParseNarrativeDate(string value)
        => DateOnly.TryParseExact(value, new[] { "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "d.M.yyyy", "dd.MM.yyyy", "yyyy-MM-dd", "d 'de' MMMM 'de' yyyy", "dd 'de' MMMM 'de' yyyy" },
            CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.None, out var date) ? date : null;

    private static string Normalize(string? value)
    {
        var decomposed = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        return new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToUpperInvariant();
    }
}
