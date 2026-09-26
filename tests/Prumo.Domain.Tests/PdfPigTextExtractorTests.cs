using Prumo.Infrastructure.Documents;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Prumo.Domain.Tests;

public sealed class PdfPigTextExtractorTests
{
    private readonly PdfPigTextExtractor extractor = new();

    [Fact]
    public async Task Extrai_texto_de_pdf_valido()
    {
        await using var stream = PdfTestData.CreatePdfStream("Documento de teste com conteudo valido para extracao.");

        var result = await extractor.ExtractAsync(stream, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Contains("Documento de teste", result.Text);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Rejeita_pdf_invalido()
    {
        await using var stream = PdfTestData.CreateInvalidPdfStream();

        var result = await extractor.ExtractAsync(stream, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Retorna_texto_vazio_quando_pdf_nao_tem_conteudo_textual()
    {
        await using var stream = PdfTestData.CreatePdfStream(string.Empty);

        var result = await extractor.ExtractAsync(stream, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, result.Text.Trim());
    }

    [Fact]
    public async Task Preserva_espaco_entre_palavras_desenhadas_separadamente()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        page.AddText("Relatorio", 12, new PdfPoint(25, 700), font);
        page.AddText("Gerencial", 12, new PdfPoint(115, 700), font);
        await using var stream = new MemoryStream(builder.Build());

        var result = await extractor.ExtractAsync(stream, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Matches(@"Relatorio\s+Gerencial", result.Text);
    }

    [Fact]
    public async Task Extrai_texto_de_xml_estruturado()
    {
        await using var stream = new MemoryStream("<?xml version=\"1.0\"?><DadosEconomicoFinanceiros><NomeFundo>VGIP11</NomeFundo><Valor>123,45</Valor></DadosEconomicoFinanceiros>"u8.ToArray());

        var result = await extractor.ExtractAsync(stream, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Contains("NomeFundo: VGIP11", result.Text);
        Assert.Contains("Valor: 123,45", result.Text);
    }

    [Fact]
    public async Task Rejeita_html_salvo_como_documento()
    {
        await using var stream = new MemoryStream("<html><body>erro</body></html>"u8.ToArray());

        var result = await extractor.ExtractAsync(stream, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
    }
}
