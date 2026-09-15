using Prumo.Infrastructure.Documents;

namespace Prumo.Domain.Tests;

public sealed class PdfPigTextExtractorTests
{
    private readonly PdfPigTextExtractor extractor = new();

    [Fact]
    public async Task Extrai_texto_de_pdf_valido()
    {
        await using var stream = PdfTestData.CreatePdfStream("Documento de teste com conteudo valido para extracao.");

        var result = await extractor.ExtractAsync(stream);

        Assert.True(result.Succeeded);
        Assert.Contains("Documento de teste", result.Text);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Rejeita_pdf_invalido()
    {
        await using var stream = PdfTestData.CreateInvalidPdfStream();

        var result = await extractor.ExtractAsync(stream);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Retorna_texto_vazio_quando_pdf_nao_tem_conteudo_textual()
    {
        await using var stream = PdfTestData.CreatePdfStream(string.Empty);

        var result = await extractor.ExtractAsync(stream);

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, result.Text.Trim());
    }

    [Fact]
    public async Task Extrai_texto_de_xml_estruturado()
    {
        await using var stream = new MemoryStream("<?xml version=\"1.0\"?><DadosEconomicoFinanceiros><NomeFundo>VGIP11</NomeFundo><Valor>123,45</Valor></DadosEconomicoFinanceiros>"u8.ToArray());

        var result = await extractor.ExtractAsync(stream);

        Assert.True(result.Succeeded);
        Assert.Contains("NomeFundo: VGIP11", result.Text);
        Assert.Contains("Valor: 123,45", result.Text);
    }

    [Fact]
    public async Task Rejeita_html_salvo_como_documento()
    {
        await using var stream = new MemoryStream("<html><body>erro</body></html>"u8.ToArray());

        var result = await extractor.ExtractAsync(stream);

        Assert.False(result.Succeeded);
    }
}
