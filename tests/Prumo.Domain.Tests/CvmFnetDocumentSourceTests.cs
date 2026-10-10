using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.DocumentSources;

namespace Prumo.Domain.Tests;

public sealed class CvmFnetDocumentSourceTests
{
    [Fact]
    public async Task UsaCnpjOficialDoAtivoSemMapaPorTicker()
    {
        const string csv = "TP_FUNDO_CLASSE;CNPJ_FUNDO_CLASSE;DT_COMPTC;DT_RECEB;TP_DOC;NM_ARQ;ID_DOC;LINK_ARQ\n" +
            "FII;11.111.111/0001-11;31/07/2026;07/08/2026;Fato Relevante;fato.pdf;125;https://dados.cvm.gov.br/documentos/fato.pdf\n";
        using var client = new HttpClient(new CsvHandler(csv));
        var source = new CvmFnetDocumentSource(client, Options.Create(new CvmFnetOptions
        {
            DocumentosEventuaisUrlTemplate = "https://dados.cvm.gov.br/eventual_{0}.csv"
        }), NullLogger<CvmFnetDocumentSource>.Instance);

        var candidates = await source.FindAsync(new[] { new DocumentSourceAsset("ABCD11", "11.111.111/0001-11") }, TestContext.Current.CancellationToken);

        Assert.Single(candidates);
        Assert.Equal("ABCD11", candidates[0].Ticker);
    }

    [Fact]
    public async Task MapeiaDocumentosEventuaisDaCvmParaOCnpjDoTicker()
    {
        const string csv = "TP_FUNDO_CLASSE;CNPJ_FUNDO_CLASSE;DENOM_SOCIAL;ID_SUBCLASSE;DT_COMPTC;DT_RECEB;TP_DOC;NM_ARQ;ID_DOC;LINK_ARQ;RESULTADO_AUDITORIA\n" +
            "FII;26.502.794/0001-85;XP LOG FII;;31/07/2026;05/08/2026;Fato Relevante;fato.pdf;123;https://dados.cvm.gov.br/documentos/fato.pdf;\n" +
            "FII;26502794000185;XP LOG FII;;31/07/2026;06/08/2026;Comunicado ao Mercado;comunicado.pdf;124;https://dados.cvm.gov.br/documentos/comunicado.pdf;\n" +
            "FII;11.111.111/0001-11;Outro FII;;31/07/2026;07/08/2026;Fato Relevante;outro.pdf;125;https://dados.cvm.gov.br/documentos/outro.pdf;\n" +
            "FII;26.502.794/0001-85;XP LOG FII;;31/07/2026;data-invalida;Relatório Gerencial;invalido.pdf;126;https://dados.cvm.gov.br/documentos/invalido.pdf;";

        using var client = new HttpClient(new CsvHandler(csv));
        var source = new CvmFnetDocumentSource(
            client,
            Options.Create(new CvmFnetOptions
            {
                DocumentosEventuaisUrlTemplate = "https://dados.cvm.gov.br/eventual_{0}.csv",
                CnpjPorTicker = new Dictionary<string, string> { ["XPLG11"] = "26.502.794/0001-85" }
            }),
            NullLogger<CvmFnetDocumentSource>.Instance);

        var candidates = await source.FindAsync(new[] { "XPLG11" }, TestContext.Current.CancellationToken);

        Assert.Equal(2, candidates.Count);
        Assert.Equal(DocumentType.RelevantFact, candidates[0].DocumentType);
        Assert.Equal(DocumentType.Communication, candidates[1].DocumentType);
        Assert.All(candidates, candidate => Assert.Equal("XPLG11", candidate.Ticker));
        Assert.Equal("123", candidates[0].SourceIdentifier);
        Assert.Equal(candidates[0].SourceUrl, candidates[0].DownloadUrl);
    }

    [Fact]
    public async Task UsaVisualizadorOficialDoFnetComoOrigemEMantemDownloadSeparado()
    {
        const string csv = "CNPJ_FUNDO_CLASSE;DT_COMPTC;DT_RECEB;TP_DOC;NM_ARQ;ID_DOC;LINK_ARQ\n" +
            "26.502.794/0001-85;31/07/2026;05/08/2026;Fato Relevante;fato.pdf;1306470;https://fnet.bmfbovespa.com.br/fnet/publico/downloadDocumento?id=1306470\n";
        using var client = new HttpClient(new CsvHandler(csv));
        var source = new CvmFnetDocumentSource(client, Options.Create(new CvmFnetOptions
        {
            DocumentosEventuaisUrlTemplate = "https://dados.cvm.gov.br/eventual_{0}.csv",
            CnpjPorTicker = new Dictionary<string, string> { ["XPLG11"] = "26.502.794/0001-85" }
        }), NullLogger<CvmFnetDocumentSource>.Instance);

        var candidate = Assert.Single(await source.FindAsync(new[] { "XPLG11" }, TestContext.Current.CancellationToken));

        Assert.Equal("https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id=1306470&cvm=true", candidate.SourceUrl);
        Assert.Equal("https://fnet.bmfbovespa.com.br/fnet/publico/downloadDocumento?id=1306470&cvm=true", candidate.DownloadUrl);
    }

    [Fact]
    public async Task PreservaCategoriaTipoEEspecieOficiaisQuandoDisponiveis()
    {
        const string csv = "CNPJ_FUNDO_CLASSE;DT_COMPTC;DT_RECEB;CATEGORIA;TP_DOC;ESPECIE;NM_ARQ;ID_DOC;LINK_ARQ\n" +
            "11.111.111/0001-11;31/07/2026;07/08/2026;Relatórios;Relatório Gerencial;;relatorio.pdf;126;https://dados.cvm.gov.br/documentos/relatorio.pdf\n";
        using var client = new HttpClient(new CsvHandler(csv));
        var source = new CvmFnetDocumentSource(client, Options.Create(new CvmFnetOptions
        {
            DocumentosEventuaisUrlTemplate = "https://dados.cvm.gov.br/eventual_{0}.csv"
        }), NullLogger<CvmFnetDocumentSource>.Instance);

        var candidate = Assert.Single(await source.FindAsync(new[] { new DocumentSourceAsset("ABCD11", "11.111.111/0001-11") }, TestContext.Current.CancellationToken));

        Assert.Equal("Relatórios", candidate.SourceCategory);
        Assert.Equal("Relatório Gerencial", candidate.SourceType);
        Assert.Null(candidate.SourceSpecies);
        Assert.Equal(DocumentType.ManagementReport, candidate.DocumentType);
    }

    [Fact]
    public async Task ComplementaCsvComDocumentoRecenteDoFnet()
    {
        const string csv = "CNPJ_FUNDO_CLASSE;DT_COMPTC;DT_RECEB;TP_DOC;NM_ARQ;ID_DOC;LINK_ARQ\n";
        const string fnet = "{\"data\":[{\"id\":\"1315141\",\"dataReferencia\":\"31/08/2026\",\"dataEntrega\":\"10/09/2026 19:58\",\"tipoDocumento\":\"Relatório Gerencial\",\"categoriaDocumento\":\"Relatórios\",\"especieDocumento\":\"\",\"status\":\"Ativo\"}]}";
        using var client = new HttpClient(new FnetHandler(csv, fnet));
        var source = new CvmFnetDocumentSource(client, Options.Create(new CvmFnetOptions
        {
            DocumentosEventuaisUrlTemplate = "https://dados.cvm.gov.br/eventual_{0}.csv",
            FnetManagerUrl = "https://fnet.test/manager",
            FnetDocumentsUrl = "https://fnet.test/documents"
        }), NullLogger<CvmFnetDocumentSource>.Instance);

        var candidate = Assert.Single(await source.FindAsync(new[] { new DocumentSourceAsset("HGBS11", "08.431.747/0001-06", AssetClass.Fii) }, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero), TestContext.Current.CancellationToken));

        Assert.Equal("1315141", candidate.SourceIdentifier);
        Assert.Equal(DocumentType.ManagementReport, candidate.DocumentType);
        Assert.Equal("https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id=1315141&cvm=true", candidate.SourceUrl);
        Assert.Equal("https://fnet.bmfbovespa.com.br/fnet/publico/downloadDocumento?id=1315141&cvm=true", candidate.DownloadUrl);
    }

    [Fact]
    public async Task PaginaTodasAsPaginasEEncontraDocumentoNaUltimaPagina()
    {
        var handler = new PagedFnetHandler(487, 1316192);
        using var client = new HttpClient(handler);
        var source = new CvmFnetDocumentSource(client, Options.Create(new CvmFnetOptions
        {
            DocumentosEventuaisUrlTemplate = "https://cvm.test/eventual_{0}.csv",
            FnetManagerUrl = "https://fnet.test/manager",
            FnetDocumentsUrl = "https://fnet.test/documents"
        }), NullLogger<CvmFnetDocumentSource>.Instance);

        var candidates = await source.FindAsync(new[] { new DocumentSourceAsset("VGIP11", "34.197.811/0001-46", AssetClass.Fii) }, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero), TestContext.Current.CancellationToken);

        var candidate = Assert.Single(candidates);
        Assert.Equal("1316192", candidate.SourceIdentifier);
        Assert.Equal(DocumentType.Other, candidate.DocumentType);
        Assert.Equal(new[] { 0, 100, 200, 300, 400 }, handler.Starts);
    }

    [Fact]
    public async Task MantemCsvQuandoFnetRetornaHtmlEmVezDeJson()
    {
        const string csv = "CNPJ_FUNDO_CLASSE;DT_COMPTC;DT_RECEB;TP_DOC;NM_ARQ;ID_DOC;LINK_ARQ\n" +
            "08.431.747/0001-06;31/07/2026;05/08/2026;Fato Relevante;fato.pdf;1306470;https://dados.cvm.gov.br/documentos/fato.pdf\n";
        using var client = new HttpClient(new FnetHandler(csv, "<html>WAF challenge</html>"));
        var source = new CvmFnetDocumentSource(client, Options.Create(new CvmFnetOptions
        {
            DocumentosEventuaisUrlTemplate = "https://dados.cvm.gov.br/eventual_{0}",
            FnetManagerUrl = "https://fnet.test/manager",
            FnetDocumentsUrl = "https://fnet.test/documents"
        }), NullLogger<CvmFnetDocumentSource>.Instance);

        var candidates = await source.FindAsync(new[]
            { new DocumentSourceAsset("HGBS11", "08.431.747/0001-06", AssetClass.Fii) }, TestContext.Current.CancellationToken);

        Assert.Single(candidates);
        Assert.Equal("1306470", candidates[0].SourceIdentifier);
    }

    [Fact]
    public async Task DescartaTickerSemResolucaoCvm()
    {
        using var client = new HttpClient(new CsvHandler("header\n"));
        var source = new CvmFnetDocumentSource(
            client,
            Options.Create(new CvmFnetOptions()),
            NullLogger<CvmFnetDocumentSource>.Instance);

        var candidates = await source.FindAsync(new[] { "BBSE3" }, TestContext.Current.CancellationToken);

        Assert.Empty(candidates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retenta520TransitorioSemPerderDocumentos(bool failManager)
    {
        var handler = new FailingFnetHandler(failures: 1, failManager: failManager);
        using var client = new HttpClient(handler);
        var logger = new RecordingLogger();
        var source = CreateFailingFnetSource(client, logger);

        var candidates = await FindTestFundAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "csv", "1000" }, candidates.Select(candidate => candidate.SourceIdentifier));
        Assert.Equal(2, handler.FailedEndpointRequests);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Theory]
    [InlineData(520, true, LogLevel.Information, 3)]
    [InlineData(520, false, LogLevel.Warning, 3)]
    [InlineData(403, true, LogLevel.Warning, 1)]
    public async Task MantemFallbackENivelDeLogConformeDisponibilidadeDoCsv(int status, bool csvAvailable, LogLevel expectedLevel, int expectedAttempts)
    {
        var handler = new FailingFnetHandler(int.MaxValue, status: status, csvAvailable: csvAvailable);
        using var client = new HttpClient(handler);
        var logger = new RecordingLogger();
        var source = CreateFailingFnetSource(client, logger);

        var candidates = await FindTestFundAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal(csvAvailable ? 1 : 0, candidates.Count);
        Assert.Equal(expectedAttempts, handler.FailedEndpointRequests);
        var failure = Assert.Single(logger.Entries, entry => entry.Message.Contains("FNET direto indisponível", StringComparison.Ordinal));
        Assert.Equal(expectedLevel, failure.Level);
        if (csvAvailable && status == 520)
            Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task PreservaPaginasAnterioresAo520RespeitandoDatasEStatus()
    {
        var handler = new FailingFnetHandler(int.MaxValue, failureStart: 100);
        using var client = new HttpClient(handler);
        var source = CreateFailingFnetSource(client, new RecordingLogger());

        var candidates = await FindTestFundAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "csv", "1000" }, candidates.Select(candidate => candidate.SourceIdentifier));
        Assert.Equal(new[] { 0, 100, 100, 100 }, handler.Starts);
    }

    [Fact]
    public async Task CancelamentoDuranteRetentativaNaoViraFallback()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var handler = new FailingFnetHandler(int.MaxValue, onFailure: () => cancellation.Cancel());
        using var client = new HttpClient(handler);
        var logger = new RecordingLogger();
        var source = CreateFailingFnetSource(client, logger);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FindTestFundAsync(source, cancellation.Token));

        Assert.Equal(1, handler.FailedEndpointRequests);
        Assert.DoesNotContain(logger.Entries, entry => entry.Message.Contains("FNET direto indisponível", StringComparison.Ordinal));
    }

    private static CvmFnetDocumentSource CreateFailingFnetSource(HttpClient client, RecordingLogger logger) => new(client, Options.Create(new CvmFnetOptions
    {
        DocumentosEventuaisUrlTemplate = "https://cvm.test/eventual_{0}.csv",
        FnetManagerUrl = "https://fnet.test/manager",
        FnetDocumentsUrl = "https://fnet.test/documents"
    }), logger);

    private static Task<IReadOnlyList<Prumo.Application.Abstractions.DocumentCandidate>> FindTestFundAsync(CvmFnetDocumentSource source, CancellationToken ct) =>
        source.FindAsync(new[] { new DocumentSourceAsset("HGBS11", "08.431.747/0001-06", AssetClass.Fii) },
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero), ct);

    private sealed class RecordingLogger : ILogger<CvmFnetDocumentSource>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class FailingFnetHandler(int failures, int status = 520, bool csvAvailable = true, int failureStart = 0, bool failManager = false, Action? onFailure = null) : HttpMessageHandler
    {
        public int FailedEndpointRequests { get; private set; }
        public List<int> Starts { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "cvm.test")
                return Response(csvAvailable ? HttpStatusCode.OK : HttpStatusCode.NotFound,
                    "CNPJ_FUNDO_CLASSE;DT_RECEB;TP_DOC;ID_DOC;LINK_ARQ\n08.431.747/0001-06;05/09/2026;Fato Relevante;csv;https://cvm.test/fato.pdf\n");

            var manager = request.RequestUri.AbsolutePath.Contains("manager", StringComparison.Ordinal);
            var start = manager ? 0 : int.Parse(request.RequestUri.Query.TrimStart('?').Split('&').Single(item => item.StartsWith("s=", StringComparison.Ordinal)).Split('=')[1]);
            if (!manager) Starts.Add(start);
            if (manager == failManager && start == failureStart)
            {
                FailedEndpointRequests++;
                if (FailedEndpointRequests <= failures)
                {
                    onFailure?.Invoke();
                    return Response((HttpStatusCode)status, "");
                }
            }

            if (manager) return Response(HttpStatusCode.OK, "{}");
            var count = failureStart == 100 ? 100 : 1;
            var rows = Enumerable.Range(0, count).Select(index => new
            {
                id = 1000 + index,
                dataEntrega = index == 2 ? "01/10/2026 12:00" : index < 2 ? "10/09/2026 12:00" : "01/01/2020 12:00",
                tipoDocumento = "Fato Relevante",
                status = index == 1 ? "Cancelado" : "Ativo"
            });
            return Response(HttpStatusCode.OK, JsonSerializer.Serialize(new { recordsTotal = failureStart == 100 ? 200 : 1, data = rows }));
        }

        private static Task<HttpResponseMessage> Response(HttpStatusCode status, string content) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content) });
    }

    private sealed class PagedFnetHandler : HttpMessageHandler
    {
        private readonly int total;
        private readonly int targetId;
        public List<int> Starts { get; } = new();

        public PagedFnetHandler(int total, int targetId)
        {
            this.total = total;
            this.targetId = targetId;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.Contains("manager", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("{}");
            if (!request.RequestUri.AbsolutePath.Contains("documents", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("CNPJ_FUNDO_CLASSE;DT_COMPTC;DT_RECEB;TP_DOC;NM_ARQ;ID_DOC;LINK_ARQ\n") });

            var start = int.Parse(request.RequestUri.Query.Split('&').First(item => item.StartsWith("?s=", StringComparison.Ordinal) || item.StartsWith("s=", StringComparison.Ordinal)).Split('=')[1]);
            Starts.Add(start);
            var count = Math.Min(100, Math.Max(0, total - start));
            var rows = Enumerable.Range(start, count)
                .Select(index => new
                {
                    id = index == total - 1 ? targetId : 100000 + index,
                    dataEntrega = index == total - 1 ? "11/09/2026 17:09" : "11/09/2020 17:09",
                    dataReferencia = index == total - 1 ? "11/09/2026" : "11/09/2020",
                    tipoDocumento = "Rendimentos e Amortizações",
                    categoriaDocumento = "Aviso aos Cotistas - Estruturado",
                    especieDocumento = "",
                    status = "Ativo"
                });
            return JsonResponse(JsonSerializer.Serialize(new { recordsTotal = total, data = rows }));
        }

        private static Task<HttpResponseMessage> JsonResponse(string content) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") });
    }

    private sealed class CsvHandler : HttpMessageHandler
    {
        private readonly byte[] content;

        public CsvHandler(string csv)
        {
            content = Encoding.Latin1.GetBytes(csv);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private sealed class FnetHandler : HttpMessageHandler
    {
        private readonly byte[] csv;
        private readonly byte[] fnet;
        public FnetHandler(string csv, string fnet) { this.csv = Encoding.Latin1.GetBytes(csv); this.fnet = Encoding.UTF8.GetBytes(fnet); }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "fnet.test" && request.RequestUri.AbsolutePath.Contains("documents", StringComparison.OrdinalIgnoreCase))
            {
                var query = request.RequestUri.Query;
                Assert.Contains("d=0", query);
                Assert.Contains("s=0", query);
                Assert.Contains("l=100", query);
                Assert.Contains("idCategoriaDocumento=0", query);
                Assert.Contains("idTipoDocumento=0", query);
                Assert.Contains("idEspecieDocumento=0", query);
                Assert.DoesNotContain("dataInicial=", query);
                Assert.DoesNotContain("dataFinal=", query);
                Assert.DoesNotContain("draw=", query);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fnet) });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(csv) });
        }
    }

    [Fact]
    public async Task MapeiaDocumentosIpeDeCompanhiaParaAcao()
    {
        const string csv = "CNPJ_Companhia;Nome_Companhia;Codigo_CVM;Data_Referencia;Categoria;Tipo;Especie;Assunto;Data_Entrega;Tipo_Apresentacao;Protocolo_Entrega;Versao;Link_Download\n" +
            "02.916.265/0001-60;Companhia;20575;2026-04-30;Fato Relevante;;;Assunto;2026-05-04;AP;020575IPE000001;1;https://www.rad.cvm.gov.br/ENET/frmDownloadDocumento.aspx?Tela=ext&numProtocolo=1\n";
        using var client = new HttpClient(new ZipHandler(csv));
        var source = new CvmFnetDocumentSource(client, Options.Create(new CvmFnetOptions
        {
            CompanhiasIpeUrlTemplate = "https://dados.cvm.gov.br/ipe_{0}.zip"
        }), NullLogger<CvmFnetDocumentSource>.Instance);

        var candidate = Assert.Single(await source.FindAsync(new[]
            { new DocumentSourceAsset("BBSE3", "02.916.265/0001-60", AssetClass.BrazilianStock) }, TestContext.Current.CancellationToken));

        Assert.Equal("BBSE3", candidate.Ticker);
        Assert.Equal(DocumentType.RelevantFact, candidate.DocumentType);
        Assert.Equal("020575IPE000001", candidate.SourceIdentifier);
        Assert.Equal(candidate.SourceUrl, candidate.DownloadUrl);
    }

    [Fact]
    public async Task IgnoraArquivoAnualAindaNaoPublicado()
    {
        using var client = new HttpClient(new NotFoundHandler());
        var source = new CvmFnetDocumentSource(client, Options.Create(new CvmFnetOptions
        {
            CompanhiasIpeUrlTemplate = "https://dados.cvm.gov.br/ipe_{0}.zip"
        }), NullLogger<CvmFnetDocumentSource>.Instance);

        var candidates = await source.FindAsync(new[]
            { new DocumentSourceAsset("BBSE3", "02.916.265/0001-60", AssetClass.BrazilianStock) }, TestContext.Current.CancellationToken);

        Assert.Empty(candidates);
    }

    private sealed class ZipHandler : HttpMessageHandler
    {
        private readonly byte[] content;

        public ZipHandler(string csv)
        {
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                var entry = archive.CreateEntry("ipe_cia_aberta_2026.csv");
                using var writer = new StreamWriter(entry.Open(), Encoding.Latin1);
                writer.Write(csv);
            }
            content = stream.ToArray();
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
