using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Lodira.Application.Abstractions;
using Lodira.Domain.Enums;

namespace Lodira.Infrastructure.DocumentSources;

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
    {
        if (tickers.Count == 0)
        {
            return Array.Empty<DocumentCandidate>();
        }

        var year = DateTimeOffset.UtcNow.Year;
        var uri = string.Format(CultureInfo.InvariantCulture, options.DocumentosEventuaisUrlTemplate, year);
        logger.LogInformation("Consultando recurso CVM {ResourceUrl} para {TickerCount} ticker(s)", uri, tickers.Count);

        try
        {
            using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            logger.LogInformation("Recurso CVM {ResourceUrl} respondeu com HTTP {StatusCode}", uri, (int)response.StatusCode);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var records = ParseCsv(Encoding.Latin1.GetString(content));
            logger.LogInformation("Recurso CVM {ResourceUrl} retornou {RecordCount} registro(s)", uri, records.Count);

            var candidates = new List<DocumentCandidate>();
            foreach (var ticker in tickers)
            {
                if (!options.CnpjPorTicker.TryGetValue(ticker, out var cnpj))
                {
                    logger.LogWarning("Ticker {Ticker} descartado: não há CNPJ CVM configurado", ticker);
                    continue;
                }

                var normalizedCnpj = NormalizeCnpj(cnpj);
                logger.LogInformation("Ticker {Ticker} resolvido para identificador CVM/CNPJ {Cnpj}", ticker, normalizedCnpj);
                var tickerRecords = records.Where(record => NormalizeCnpj(record.Cnpj) == normalizedCnpj).ToList();
                logger.LogInformation("Recurso CVM mapeou {RecordCount} registro(s) para o ticker {Ticker}", tickerRecords.Count, ticker);

                foreach (var record in tickerRecords)
                {
                    var documentUrl = record.LinkArquivo?.Trim();
                    if (string.IsNullOrWhiteSpace(documentUrl) || !TryParseDate(record.DataRecebimento ?? record.DataCompetencia, out var publishedAt))
                    {
                        logger.LogWarning("Registro CVM descartado para {Ticker}: URL ou data inválida, identificador={DocumentId}", ticker, record.IdDocumento);
                        continue;
                    }

                    candidates.Add(new DocumentCandidate(
                        ticker,
                        MapDocumentType(record.TipoDocumento),
                        publishedAt,
                        string.IsNullOrWhiteSpace(record.IdDocumento) ? record.NomeArquivo : record.IdDocumento,
                        documentUrl,
                        documentUrl));
                }
            }

            logger.LogInformation("Fonte {SourceName} criou {CandidateCount} candidato(s)", Name, candidates.Count);
            return candidates;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Erro ao consultar ou desserializar a fonte {SourceName} em {RequestUrl}", Name, uri);
            throw;
        }
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
                Get(fields, index, "CNPJ_FUNDO_CLASSE"),
                Get(fields, index, "DT_COMPTC"),
                Get(fields, index, "DT_RECEB"),
                Get(fields, index, "TP_DOC"),
                Get(fields, index, "NM_ARQ"),
                Get(fields, index, "ID_DOC"),
                Get(fields, index, "LINK_ARQ")))
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

    private static string NormalizeCnpj(string? value) =>
        new string((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static bool TryParseDate(string? value, out DateTimeOffset date)
    {
        return DateTimeOffset.TryParseExact(value?.Trim(), new[] { "dd/MM/yyyy", "yyyy-MM-dd", "dd/MM/yyyy HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date);
    }

    private static DocumentType MapDocumentType(string? value)
    {
        var normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        normalized = new string(normalized.Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark).ToArray()).ToUpperInvariant();
        return normalized switch
        {
            var type when type.Contains("FATO RELEVANTE") => DocumentType.RelevantFact,
            var type when type.Contains("COMUNICADO") => DocumentType.Communication,
            var type when type.Contains("RELATORIO GERENCIAL") => DocumentType.ManagementReport,
            _ => DocumentType.Other
        };
    }

    private sealed record CvmDocumentRecord(string? Cnpj, string? DataCompetencia, string? DataRecebimento, string? TipoDocumento, string? NomeArquivo, string? IdDocumento, string? LinkArquivo);
}
