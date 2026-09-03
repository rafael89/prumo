using Prumo.Application.Abstractions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;

namespace Prumo.Infrastructure.Documents;

public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public Task<PdfTextExtractionResult> ExtractAsync(Stream pdf, CancellationToken cancellationToken = default)
    {
        if (pdf is null) throw new ArgumentNullException(nameof(pdf));
        if (!pdf.CanRead) return Task.FromResult(new PdfTextExtractionResult(false, string.Empty, "O arquivo PDF não pode ser lido."));

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var document = PdfDocument.Open(pdf);
            var text = string.Join(Environment.NewLine, document.GetPages().Select(page => page.Text));
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PdfTextExtractionResult(true, text, null));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfDocumentFormatException exception)
        {
            return Task.FromResult(new PdfTextExtractionResult(false, string.Empty, $"Falha ao extrair texto: {exception.Message}"));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or ArgumentException)
        {
            return Task.FromResult(new PdfTextExtractionResult(false, string.Empty, $"Falha ao extrair texto: {exception.Message}"));
        }
    }
}
