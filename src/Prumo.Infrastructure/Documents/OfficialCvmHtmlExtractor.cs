using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Prumo.Infrastructure.Documents;

public static partial class OfficialCvmHtmlExtractor
{
    [GeneratedRegex(@"<\s*(script|style)\b[^>]*>.*?<\s*/\s*\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<\s*/?\s*(?:br|p|div|tr|td|th|li|h[1-6])\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockTags();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex Tags();

    public static bool IsOfficialCvmUrl(Uri? uri)
        => uri is { Scheme: "https" } && (uri.Host.Equals("www.rad.cvm.gov.br", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("rad.cvm.gov.br", StringComparison.OrdinalIgnoreCase))
            && (uri.AbsolutePath.StartsWith("/ENETCONSULTA/", StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath.StartsWith("/ENET/", StringComparison.OrdinalIgnoreCase));

    public static bool TryExtract(byte[] payload, out string text)
    {
        text = string.Empty;
        if (payload.Length is < 150 or > 5_000_000) return false;
        var html = Encoding.UTF8.GetString(payload);
        if (!html.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || html.Contains("type=\"password\"", StringComparison.OrdinalIgnoreCase)
            || html.Contains("captcha", StringComparison.OrdinalIgnoreCase)) return false;
        var withoutScripts = ScriptOrStyle().Replace(html, " ");
        var withLines = BlockTags().Replace(withoutScripts, "\n");
        var plain = WebUtility.HtmlDecode(Tags().Replace(withLines, " "));
        text = string.Join('\n', plain.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line)));
        return text.Length >= 100 && (text.Contains("CNPJ", StringComparison.OrdinalIgnoreCase)
            || text.Contains("companhia", StringComparison.OrdinalIgnoreCase));
    }
}
