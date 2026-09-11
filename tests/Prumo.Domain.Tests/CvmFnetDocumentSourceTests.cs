using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Prumo.Domain.Enums;
using Prumo.Application.Abstractions;
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

        var candidates = await source.FindAsync(new[] { new DocumentSourceAsset("ABCD11", "11.111.111/0001-11") });

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

        var candidates = await source.FindAsync(new[] { "XPLG11" });

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

        var candidate = Assert.Single(await source.FindAsync(new[] { "XPLG11" }));

        Assert.Equal("https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id=1306470", candidate.SourceUrl);
        Assert.Equal("https://fnet.bmfbovespa.com.br/fnet/publico/downloadDocumento?id=1306470", candidate.DownloadUrl);
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

        var candidate = Assert.Single(await source.FindAsync(new[] { new DocumentSourceAsset("ABCD11", "11.111.111/0001-11") }));

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

        var candidate = Assert.Single(await source.FindAsync(new[] { new DocumentSourceAsset("HGBS11", "08.431.747/0001-06", AssetClass.Fii) }));

        Assert.Equal("1315141", candidate.SourceIdentifier);
        Assert.Equal(DocumentType.ManagementReport, candidate.DocumentType);
        Assert.Equal("https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id=1315141", candidate.SourceUrl);
        Assert.Equal("https://fnet.bmfbovespa.com.br/fnet/publico/downloadDocumento?id=1315141", candidate.DownloadUrl);
    }

    [Fact]
    public async Task DescartaTickerSemResolucaoCvm()
    {
        using var client = new HttpClient(new CsvHandler("header\n"));
        var source = new CvmFnetDocumentSource(
            client,
            Options.Create(new CvmFnetOptions()),
            NullLogger<CvmFnetDocumentSource>.Instance);

        var candidates = await source.FindAsync(new[] { "BBSE3" });

        Assert.Empty(candidates);
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
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(request.RequestUri!.Host == "fnet.test" ? fnet : csv) });
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
            { new DocumentSourceAsset("BBSE3", "02.916.265/0001-60", AssetClass.BrazilianStock) }));

        Assert.Equal("BBSE3", candidate.Ticker);
        Assert.Equal(DocumentType.RelevantFact, candidate.DocumentType);
        Assert.Equal("020575IPE000001", candidate.SourceIdentifier);
        Assert.Equal(candidate.SourceUrl, candidate.DownloadUrl);
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
}
