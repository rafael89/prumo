using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
        => await FindAsync(assets, null, null, cancellationToken);

    public async Task<IReadOnlyList<DocumentCandidate>> FindAsync(
        IReadOnlyCollection<DocumentSourceAsset> assets,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
        => await FindAsync(assets, (DateTimeOffset?)from, (DateTimeOffset?)to, cancellationToken);

    private async Task<IReadOnlyList<DocumentCandidate>> FindAsync(
        IReadOnlyCollection<DocumentSourceAsset> assets,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (assets.Count == 0)
        {
            return Array.Empty<DocumentCandidate>();
        }

        var year = DateTimeOffset.UtcNow.Year;
        var candidates = new List<DocumentCandidate>();
        var directFailureWarnings = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in assets.GroupBy(asset => asset.AssetClass == AssetClass.BrazilianStock))
        {
            var isStock = group.Key;
            var groupAssets = group.ToArray();
            var template = isStock ? options.CompanhiasIpeUrlTemplate : options.DocumentosEventuaisUrlTemplate;
            var uri = string.Format(CultureInfo.InvariantCulture, template, year);
            logger.LogInformation("Consultando recurso CVM {ResourceUrl} para {TickerCount} ativo(s)", uri, groupAssets.Length);

            try
            {
                IReadOnlyList<CvmDocumentRecord> records;
                try
                {
                    using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    records = isStock
                        ? await ParseZipCsvAsync(content, cancellationToken)
                        : ParseCsv(Encoding.Latin1.GetString(content));
                }
                catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // CVM publishes the annual archive only after the year has
                    // progressed. Recent documents can still be obtained from
                    // the direct ENET/FNET endpoints below.
                    logger.LogInformation("Arquivo anual CVM ainda não publicado; continuando pela consulta direta: {RequestUrl}", uri);
                    records = Array.Empty<CvmDocumentRecord>();
                }
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
                    if (isStock && options.EnetDirectDiscoveryEnabled)
                    {
                        var directRecords = await FindRecentEnetRecordsAsync(asset, normalizedCnpj, from, to, cancellationToken, directFailureWarnings);
                        tickerRecords.AddRange(directRecords);
                    }
                    if (!isStock && options.FnetDirectDiscoveryEnabled)
                    {
                        var directRecords = await FindRecentFnetRecordsAsync(asset, normalizedCnpj, from, to, cancellationToken, directFailureWarnings);
                        tickerRecords.AddRange(directRecords);
                    }
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
                            ToFnetDownloadUrl(documentUrl),
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

    private async Task<IReadOnlyList<CvmDocumentRecord>> FindRecentEnetRecordsAsync(DocumentSourceAsset asset, string normalizedCnpj, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct, ISet<string> directFailureWarnings)
    {
        if (normalizedCnpj.Length != 14) return Array.Empty<CvmDocumentRecord>();
        try
        {
            var companyCode = await ResolveEnetCompanyCodeAsync(normalizedCnpj, ct);
            if (string.IsNullOrWhiteSpace(companyCode)) return Array.Empty<CvmDocumentRecord>();
            var now = to ?? DateTimeOffset.UtcNow;
            var queryFrom = from ?? now.AddDays(-Math.Max(1, options.EnetDirectLookbackDays));
            var body = new
            {
                dataDe = queryFrom.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                dataAte = now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                empresa = companyCode, setorAtividade = "-1", categoriaEmissor = "-1", situacaoEmissor = "-1",
                tipoParticipante = "-1", dataReferencia = "", categoria = "IPE_-1_-1_-1", periodo = "2",
                horaIni = "", horaFim = "", palavraChave = "", ultimaDtRef = "false", tipoEmpresa = "0", token = "", versaoCaptcha = ""
            };
            using var response = await httpClient.PostAsJsonAsync($"{options.EnetConsultaUrl}/ListarDocumentos", body, ct);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            if (!json.RootElement.TryGetProperty("d", out var result) || result.ValueKind != JsonValueKind.Object
                || !result.TryGetProperty("dados", out var data) || data.ValueKind != JsonValueKind.String
                || result.TryGetProperty("SolicitarCaptcha", out var captcha) && string.Equals(captcha.GetString(), "S", StringComparison.OrdinalIgnoreCase))
                return Array.Empty<CvmDocumentRecord>();

            var cutoff = queryFrom;
            var records = new List<CvmDocumentRecord>();
            foreach (var row in (data.GetString() ?? string.Empty).Split("&*", StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = row.Split("$&");
                if (fields.Length < 11 || !TryParseEnetDate(fields[6], out var delivered) || delivered < cutoff || delivered > now.AddMinutes(5)) continue;
                if (fields[7].StartsWith("C", StringComparison.OrdinalIgnoreCase)) continue;
                var action = fields[10];
                var match = System.Text.RegularExpressions.Regex.Match(action, "OpenDownloadDocumentos\\(([^)]*)\\)");
                if (!match.Success) continue;
                var parameters = match.Groups[1].Value.Split(',').Select(value => value.Trim().Trim('\'', '"')).ToArray();
                if (parameters.Length < 4) continue;
                var protocol = parameters[2];
                var sequence = parameters[0];
                var version = parameters[1];
                var type = parameters[3];
                var download = $"https://www.rad.cvm.gov.br/ENETCONSULTA/frmDownloadDocumento.aspx?Tela=ext&numSequencia={Uri.EscapeDataString(sequence)}&numVersao={Uri.EscapeDataString(version)}&numProtocolo={Uri.EscapeDataString(protocol)}&descTipo={Uri.EscapeDataString(type)}&CodigoInstituicao=1";
                records.Add(new CvmDocumentRecord(normalizedCnpj, fields[5], fields[6], fields[3], fields[2], fields[4], null, protocol, download));
            }
            logger.LogInformation("ENET direto mapeou {RecordCount} documento(s) recentes para {Ticker}", records.Count, asset.Ticker);
            return records;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (HttpRequestException exception)
        {
            if (directFailureWarnings.Add($"ENET:http:{(int?)exception.StatusCode ?? 0}"))
                logger.LogDebug("ENET direto indisponível; mantendo descoberta pelo IPE (primeiro ticker afetado: {Ticker}, HTTP {StatusCode})", asset.Ticker, (int?)exception.StatusCode ?? 0);
            return Array.Empty<CvmDocumentRecord>();
        }
        catch (JsonException)
        {
            if (directFailureWarnings.Add("ENET:json"))
                logger.LogDebug("ENET direto retornou uma resposta inválida; mantendo descoberta pelo IPE (primeiro ticker afetado: {Ticker})", asset.Ticker);
            return Array.Empty<CvmDocumentRecord>();
        }
        catch (Exception exception)
        {
            if (directFailureWarnings.Add("ENET:other"))
                logger.LogDebug(exception, "ENET direto indisponível; mantendo descoberta pelo IPE (primeiro ticker afetado: {Ticker})", asset.Ticker);
            return Array.Empty<CvmDocumentRecord>();
        }
    }

    private async Task<string?> ResolveEnetCompanyCodeAsync(string cnpj, CancellationToken ct)
    {
        using var response = await httpClient.PostAsJsonAsync($"{options.EnetConsultaUrl}/ConsultarCompanhiaAbertaCNPJ", new { cnpj }, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var value = json.RootElement.TryGetProperty("d", out var result) ? result.GetString() : null;
        var match = value is null ? null : System.Text.RegularExpressions.Regex.Match(value, "(?:^|,)C_([0-9]+)");
        return match?.Success == true ? match.Groups[1].Value : null;
    }

    private static bool TryParseEnetDate(string? value, out DateTimeOffset date)
        => DateTimeOffset.TryParseExact(value?.Trim(), new[] { "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date);

    private async Task<IReadOnlyList<CvmDocumentRecord>> FindRecentFnetRecordsAsync(DocumentSourceAsset asset, string normalizedCnpj, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct, ISet<string> directFailureWarnings)
    {
        if (normalizedCnpj.Length != 14) return Array.Empty<CvmDocumentRecord>();

        try
        {
            // FNET establishes the session used by the grid when its manager
            // page is opened. Without this request the JSON endpoint may
            // return the upstream WAF page instead of the grid payload.
            using var managerRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"{options.FnetManagerUrl}?cnpjFundo={Uri.EscapeDataString(normalizedCnpj)}");
            managerRequest.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            managerRequest.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0 Safari/537.36");
            using var managerResponse = await httpClient.SendAsync(managerRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            managerResponse.EnsureSuccessStatusCode();

            using var request = new HttpRequestMessage(HttpMethod.Get, BuildFnetDocumentsUrl(normalizedCnpj, from, to));
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*; q=0.01");
            request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            request.Headers.TryAddWithoutValidation("Referer", $"{options.FnetManagerUrl}?cnpjFundo={normalizedCnpj}");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0 Safari/537.36");
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!TryGetArray(json.RootElement, "data", out var data)
                && !TryGetArray(json.RootElement, "aaData", out data))
                return Array.Empty<CvmDocumentRecord>();

            var upperBound = to ?? DateTimeOffset.UtcNow;
            var cutoff = from ?? upperBound.AddDays(-Math.Max(1, options.FnetDirectLookbackDays));
            var records = new List<CvmDocumentRecord>();
            foreach (var item in data.EnumerateArray())
            {
                var id = ReadString(item, "id");
                var delivered = ReadString(item, "dataEntrega");
                var reference = ReadString(item, "dataReferencia");
                if (string.IsNullOrWhiteSpace(id) || !TryParseFnetDate(delivered, out var publishedAt) || publishedAt < cutoff || publishedAt > upperBound.AddMinutes(5)) continue;
                if (ReadString(item, "status")?.StartsWith("C", StringComparison.OrdinalIgnoreCase) == true) continue;

                records.Add(new CvmDocumentRecord(
                    normalizedCnpj,
                    reference,
                    delivered,
                    ReadString(item, "tipoDocumento"),
                    ReadString(item, "categoriaDocumento"),
                    ReadString(item, "especieDocumento"),
                    null,
                    id,
                    $"https://fnet.bmfbovespa.com.br/fnet/publico/downloadDocumento?id={Uri.EscapeDataString(id)}&cvm=true"));
            }
            logger.LogInformation("FNET direto mapeou {RecordCount} documento(s) recentes para {Ticker}", records.Count, asset.Ticker);
            return records;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (HttpRequestException exception)
        {
            if (directFailureWarnings.Add($"FNET:http:{(int?)exception.StatusCode ?? 0}"))
                logger.LogDebug("FNET direto indisponível; mantendo descoberta pelo CSV (primeiro ticker afetado: {Ticker}, HTTP {StatusCode})", asset.Ticker, (int?)exception.StatusCode ?? 0);
            return Array.Empty<CvmDocumentRecord>();
        }
        catch (JsonException)
        {
            if (directFailureWarnings.Add("FNET:json"))
                logger.LogDebug("FNET direto retornou uma resposta não-JSON; mantendo descoberta pelo CSV (primeiro ticker afetado: {Ticker})", asset.Ticker);
            return Array.Empty<CvmDocumentRecord>();
        }
        catch (Exception exception)
        {
            // The public FNET grid is protected by an upstream WAF in some
            // environments. Its failure must not interrupt the reliable CVM
            // CSV discovery path.
            if (directFailureWarnings.Add("FNET:other"))
                logger.LogDebug(exception, "FNET direto indisponível; mantendo descoberta pelo CSV (primeiro ticker afetado: {Ticker})", asset.Ticker);
            return Array.Empty<CvmDocumentRecord>();
        }
    }

    private string BuildFnetDocumentsUrl(string cnpj, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        var upperBound = to ?? DateTimeOffset.UtcNow;
        var lowerBound = from ?? upperBound.AddDays(-Math.Max(1, options.FnetDirectLookbackDays));
        var query = new Dictionary<string, string>
        {
            // The public FNET endpoint uses its legacy paging names (d/s/l),
            // rather than the DataTables names (draw/start/length).
            ["d"] = "0", ["s"] = "0", ["l"] = "100",
            ["tipoFundo"] = "1", ["cnpj"] = cnpj, ["cnpjFundo"] = cnpj,
            ["idCategoriaDocumento"] = "0", ["idTipoDocumento"] = "0", ["idEspecieDocumento"] = "0",
            ["situacao"] = "", ["dataReferencia"] = "",
            ["dataInicial"] = lowerBound.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["dataFinal"] = upperBound.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["isSession"] = "true", ["paginaCertificados"] = "false"
        };
        return options.FnetDocumentsUrl + "?" + string.Join("&", query.Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));
    }

    private static bool TryGetArray(JsonElement element, string property, out JsonElement value)
    {
        foreach (var candidate in element.EnumerateObject())
        {
            if (string.Equals(candidate.Name, property, StringComparison.OrdinalIgnoreCase)
                && candidate.Value.ValueKind == JsonValueKind.Array)
            {
                value = candidate.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string? ReadString(JsonElement item, string property)
    {
        var value = item.EnumerateObject()
            .FirstOrDefault(candidate => string.Equals(candidate.Name, property, StringComparison.OrdinalIgnoreCase)).Value;
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : value.ToString().Trim();
    }

    private static bool TryParseFnetDate(string? value, out DateTimeOffset date)
        => DateTimeOffset.TryParseExact(value?.Trim(), new[]
        {
            "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy",
            "yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK"
        }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date);

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
            : $"https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id={Uri.EscapeDataString(id)}&cvm=true";
    }

    private static string ToFnetDownloadUrl(string documentUrl)
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
            : $"https://fnet.bmfbovespa.com.br/fnet/publico/downloadDocumento?id={Uri.EscapeDataString(id)}&cvm=true";
    }

    private static bool TryParseDate(string? value, out DateTimeOffset date)
    {
        return DateTimeOffset.TryParseExact(value?.Trim(), new[] { "dd/MM/yyyy", "yyyy-MM-dd", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date);
    }

    internal static class CvmDocumentTypeMapper
    {
        public static DocumentType Map(string? category, string? type, string? species) =>
            DocumentClassificationPolicy.Map(category, type, species);
    }

    private sealed record CvmDocumentRecord(string? Cnpj, string? DataCompetencia, string? DataRecebimento, string? TipoDocumento, string? Categoria, string? Especie, string? NomeArquivo, string? IdDocumento, string? LinkArquivo);
}
