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
}
