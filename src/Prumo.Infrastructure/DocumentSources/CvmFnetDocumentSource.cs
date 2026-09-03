using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prumo.Application.Abstractions;
using Prumo.Application.Documents;
using Prumo.Domain.Enums;

namespace Prumo.Infrastructure.DocumentSources;

public sealed class CvmFnetDocumentSource : IDocumentSource
{
    private readonly HttpClient httpClient;
    private readonly CvmFnetOptions options;
    private readonly ILogger<CvmFnetDocumentSource> logger;

    public CvmFnetDocumentSource(
        HttpClient httpClient,
        IOptions<CvmFnetOptions> options,
        ILogger<CvmFnetDocumentSource> logger)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.logger = logger;
    }

    public string Name => "CVM/FNET";

    public async Task<IReadOnlyList<DocumentCandidate>> FindAsync(IReadOnlyCollection<string> tickers, CancellationToken cancellationToken = default)
        => await FindAsync(tickers.Select(ticker => new DocumentSourceAsset(ticker, null)).ToArray(), cancellationToken);

    public async Task<IReadOnlyList<DocumentCandidate>> FindAsync(IReadOnlyCollection<DocumentSourceAsset> assets, CancellationToken cancellationToken = default)
    {
        if (assets.Count == 0)
        {
            return Array.Empty<DocumentCandidate>();
        }

        var year = DateTimeOffset.UtcNow.Year;
        var candidates = new List<DocumentCandidate>();

        foreach (var group in assets.GroupBy(asset => asset.AssetClass == AssetClass.BrazilianStock))
        {
            var isStock = group.Key;
            var groupAssets = group.ToArray();
            var template = isStock ? options.CompanhiasIpeUrlTemplate : options.DocumentosEventuaisUrlTemplate;
            var uri = string.Format(CultureInfo.InvariantCulture, template, year);
            logger.LogInformation("Consultando recurso CVM {ResourceUrl} para {TickerCount} ativo(s)", uri, groupAssets.Length);

            try
            {
                using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var records = isStock
                    ? await ParseZipCsvAsync(content, cancellationToken)
                    : ParseCsv(Encoding.Latin1.GetString(content));
                logger.LogInformation("Recurso CVM {ResourceUrl} retornou {RecordCount} registro(s)", uri, records.Count);

                foreach (var asset in groupAssets)
                {
                    var cnpj = asset.Cnpj;
                    if (string.IsNullOrWhiteSpace(cnpj)) options.CnpjPorTicker.TryGetValue(asset.Ticker, out cnpj);
                    if (string.IsNullOrWhiteSpace(cnpj))
                    {
                        logger.LogWarning("Ticker {Ticker} descartado: não há CNPJ CVM configurado", asset.Ticker);
                        continue;
                    }

                    var normalizedCnpj = NormalizeCnpj(cnpj);
                    var tickerRecords = records.Where(record => NormalizeCnpj(record.Cnpj) == normalizedCnpj).ToList();
                    logger.LogInformation("Recurso CVM mapeou {RecordCount} registro(s) para o ticker {Ticker}", tickerRecords.Count, asset.Ticker);

                    foreach (var record in tickerRecords)
                    {
                        var documentUrl = record.LinkArquivo?.Trim();
                        if (string.IsNullOrWhiteSpace(documentUrl) || !TryParseDate(record.DataRecebimento ?? record.DataCompetencia, out var publishedAt))
                        {
                            logger.LogWarning("Registro CVM descartado para {Ticker}: URL ou data inválida, identificador={DocumentId}", asset.Ticker, record.IdDocumento);
                            continue;
                        }

                        candidates.Add(new DocumentCandidate(
                            asset.Ticker,
                            CvmDocumentTypeMapper.Map(record.Categoria, record.TipoDocumento, record.Especie),
                            publishedAt,
                            string.IsNullOrWhiteSpace(record.IdDocumento) ? record.NomeArquivo : record.IdDocumento,
                            ToOfficialViewerUrl(documentUrl),
                            documentUrl,
                            record.Categoria,
                            record.TipoDocumento,
                            record.Especie));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Erro ao consultar a fonte {SourceName} em {RequestUrl}", Name, uri);
                throw;
            }
        }

        logger.LogInformation("Fonte {SourceName} criou {CandidateCount} candidato(s)", Name, candidates.Count);
        return candidates;
    }

    private static async Task<List<CvmDocumentRecord>> ParseZipCsvAsync(byte[] content, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault(item => item.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("O pacote IPE da CVM não contém um CSV.");
        using var reader = new StreamReader(entry.Open(), Encoding.Latin1);
        return ParseCsv(await reader.ReadToEndAsync(cancellationToken));
    }
    private static List<CvmDocumentRecord> ParseCsv(string content)
    {
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return new List<CvmDocumentRecord>();

        var headers = ParseCsvLine(lines[0]);
        var index = headers
            .Select((header, position) => new { Header = header.Trim(), position })
            .ToDictionary(item => item.Header, item => item.position, StringComparer.OrdinalIgnoreCase);

        return lines.Skip(1)
            .Select(line => ParseCsvLine(line))
            .Where(fields => fields.Count > 0)
            .Select(fields => new CvmDocumentRecord(
                GetAny(fields, index, "CNPJ_FUNDO_CLASSE", "CNPJ_Companhia", "CNPJ_COMPANHIA"),
                GetAny(fields, index, "DT_COMPTC", "Data_Referencia"),
                GetAny(fields, index, "DT_RECEB", "Data_Entrega"),
                GetAny(fields, index, "TP_DOC", "Tipo", "TIPO"),
                GetAny(fields, index, "CATEGORIA", "CATEGORIA_DOCUMENTO", "DS_CATEGORIA", "Categoria"),
                GetAny(fields, index, "ESPECIE", "ESPECIE_DOCUMENTO", "TP_ESPECIE", "DS_ESPECIE", "Especie"),
                GetAny(fields, index, "NM_ARQ", "Nome_Arquivo"),
                GetAny(fields, index, "ID_DOC", "Protocolo_Entrega", "ID_DOCUMENTO"),
                GetAny(fields, index, "LINK_ARQ", "Link_Download")))
            .ToList();
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];
            if (character == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ';' && !quoted)
            {
                fields.Add(value.ToString());
                value.Clear();
            }
            else
            {
                value.Append(character);
            }
        }
        fields.Add(value.ToString());
        return fields;
    }

    private static string? Get(IReadOnlyList<string> fields, IReadOnlyDictionary<string, int> index, string name) =>
        index.TryGetValue(name, out var position) && position < fields.Count ? fields[position] : null;

    private static string? GetAny(IReadOnlyList<string> fields, IReadOnlyDictionary<string, int> index, params string[] names) =>
        names.Select(name => Get(fields, index, name)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string NormalizeCnpj(string? value) =>
        new string((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string ToOfficialViewerUrl(string documentUrl)
    {
        if (!Uri.TryCreate(documentUrl, UriKind.Absolute, out var uri)
            || !uri.Host.Contains("fnet.bmfbovespa.com.br", StringComparison.OrdinalIgnoreCase)) return documentUrl;

        var id = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(parameter => parameter.Split('=', 2))
            .Where(parameter => parameter.Length == 2 && string.Equals(parameter[0], "id", StringComparison.OrdinalIgnoreCase))
            .Select(parameter => Uri.UnescapeDataString(parameter[1]))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return string.IsNullOrWhiteSpace(id)
            ? documentUrl
            : $"https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id={Uri.EscapeDataString(id)}";
    }

    private static bool TryParseDate(string? value, out DateTimeOffset date)
    {
        return DateTimeOffset.TryParseExact(value?.Trim(), new[] { "dd/MM/yyyy", "yyyy-MM-dd", "dd/MM/yyyy HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date);
    }

    internal static class CvmDocumentTypeMapper
    {
        public static DocumentType Map(string? category, string? type, string? species) =>
            DocumentClassificationPolicy.Map(category, type, species);
    }

    private sealed record CvmDocumentRecord(string? Cnpj, string? DataCompetencia, string? DataRecebimento, string? TipoDocumento, string? Categoria, string? Especie, string? NomeArquivo, string? IdDocumento, string? LinkArquivo);
}
