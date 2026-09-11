using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Prumo.Domain.Enums;

namespace Prumo.Infrastructure.DocumentSources;

public sealed record CvmFiiStructuredAsset(string Ticker, string? Cnpj, AssetType AssetType);

public sealed record CvmFiiMonthlyRecord(
    string FundClassCnpj,
    DateOnly ReferenceDate,
    int SourceVersion,
    decimal? NetAssetValue,
    decimal? NavPerShare,
    decimal? Cash,
    string SourceReference,
    string SchemaVersion,
    string Fingerprint);

public sealed class CvmFiiMonthlyStructuredSource
{
    public const string SchemaVersion = "cvm-fii-inf-mensal-v2026-01";
    private readonly HttpClient httpClient;
    private readonly CvmFnetOptions options;

    public CvmFiiMonthlyStructuredSource(HttpClient httpClient, Microsoft.Extensions.Options.IOptions<CvmFnetOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
    }

    public async Task<IReadOnlyList<CvmFiiMonthlyRecord>> FindAsync(IReadOnlyCollection<CvmFiiStructuredAsset> assets, CancellationToken cancellationToken = default)
    {
        var cnpjs = assets.Where(x => x.AssetType == AssetType.Fii)
            .Select(x => NormalizeCnpj(x.Cnpj)).Where(x => x.Length == 14).ToHashSet();
        if (cnpjs.Count == 0) return Array.Empty<CvmFiiMonthlyRecord>();

        var year = DateTimeOffset.UtcNow.Year;
        var url = string.Format(CultureInfo.InvariantCulture, options.InformeMensalFiiUrlTemplate, year);
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return ParseZip(bytes, cnpjs, url);
    }

    public static IReadOnlyList<CvmFiiMonthlyRecord> ParseZip(byte[] content, IReadOnlySet<string> cnpjs, string sourceReference)
    {
        using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        var active = ReadCsv(archive, "inf_mensal_fii_ativo_passivo_");
        var complement = ReadCsv(archive, "inf_mensal_fii_complemento_");
        var rows = new Dictionary<(string Cnpj, DateOnly Date), MutableRow>();
        foreach (var row in active.Concat(complement))
        {
            var cnpj = NormalizeCnpj(row.Get("CNPJ_Fundo_Classe"));
            if (!cnpjs.Contains(cnpj) || !DateOnly.TryParseExact(row.Get("Data_Referencia"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            var key = (cnpj, date);
            if (!rows.TryGetValue(key, out var target)) rows[key] = target = new(cnpj, date, ParseInt(row.Get("Versao")));
            target.SourceVersion = Math.Max(target.SourceVersion, ParseInt(row.Get("Versao")));
            target.NetAssetValue ??= ParseDecimal(row.Get("Patrimonio_Liquido"));
            target.NavPerShare ??= ParseDecimal(row.Get("Valor_Patrimonial_Cotas"));
            target.Cash ??= ParseDecimal(row.Get("Disponibilidades"));
        }

        return rows.Values.Select(row =>
        {
            var payload = $"{row.Cnpj}|{row.Date:yyyy-MM-dd}|{row.SourceVersion}|{row.NetAssetValue?.ToString(CultureInfo.InvariantCulture)}|{row.NavPerShare?.ToString(CultureInfo.InvariantCulture)}|{row.Cash?.ToString(CultureInfo.InvariantCulture)}";
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
            return new CvmFiiMonthlyRecord(row.Cnpj, row.Date, row.SourceVersion, row.NetAssetValue, row.NavPerShare, row.Cash,
                $"{sourceReference}#cnpj={row.Cnpj}&referenceDate={row.Date:yyyy-MM-dd}&version={row.SourceVersion}", SchemaVersion, fingerprint);
        }).OrderBy(x => x.FundClassCnpj).ThenBy(x => x.ReferenceDate).ToArray();
    }

    private static List<CsvRow> ReadCsv(ZipArchive archive, string prefix)
    {
        var entry = archive.Entries.FirstOrDefault(x => x.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && x.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Arquivo CVM não encontrado: {prefix}");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true);
        var lines = reader.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return [];
        var headers = ParseLine(lines[0]);
        return lines.Skip(1).Select(line => new CsvRow(headers, ParseLine(line))).ToList();
    }

    private static List<string> ParseLine(string line)
    {
        var fields = new List<string>(); var value = new StringBuilder(); var quoted = false;
        foreach (var character in line)
        {
            if (character == '"') quoted = !quoted;
            else if (character == ';' && !quoted) { fields.Add(value.ToString()); value.Clear(); }
            else value.Append(character);
        }
        fields.Add(value.ToString());
        return fields;
    }

    private static decimal? ParseDecimal(string? value) => decimal.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;
    private static int ParseInt(string? value) => int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static string NormalizeCnpj(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private sealed class MutableRow(string cnpj, DateOnly date, int sourceVersion)
    {
        public string Cnpj { get; } = cnpj;
        public DateOnly Date { get; } = date;
        public int SourceVersion { get; set; } = sourceVersion;
        public decimal? NetAssetValue { get; set; }
        public decimal? NavPerShare { get; set; }
        public decimal? Cash { get; set; }
    }

    private sealed class CsvRow(IReadOnlyList<string> headers, IReadOnlyList<string> values)
    {
        public string? Get(string name)
        {
            for (var i = 0; i < headers.Count; i++)
                if (string.Equals(headers[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return i < values.Count ? values[i] : null;
            return null;
        }
    }
}
