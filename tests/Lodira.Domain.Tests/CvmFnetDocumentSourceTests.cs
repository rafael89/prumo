using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Lodira.Domain.Enums;
using Lodira.Application.Abstractions;
using Lodira.Infrastructure.DocumentSources;

namespace Lodira.Domain.Tests;

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
}
