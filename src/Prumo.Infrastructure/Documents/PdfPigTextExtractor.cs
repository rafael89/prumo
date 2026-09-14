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
            using var buffer = new MemoryStream();
            pdf.CopyTo(buffer);
            var bytes = buffer.ToArray();

            try
            {
                var text = ExtractText(bytes, ParsingOptions.LenientParsingOff);
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new PdfTextExtractionResult(true, text, null));
            }
            catch (Exception exception) when (IsRecoverablePdfParseFailure(exception))
            {
                // Some CVM PDFs are structurally valid enough to contain text
                // but violate details of the PDF specification. Retry with
                // PdfPig's permissive parser before rejecting the document.
                var options = new ParsingOptions
                {
                    UseLenientParsing = true,
                    SkipMissingFonts = true
                };
                var text = ExtractText(bytes, options);
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new PdfTextExtractionResult(true, text, null));
            }
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

    private static string ExtractText(byte[] bytes, ParsingOptions options)
    {
        using var document = PdfDocument.Open(bytes, options);
        return string.Join(Environment.NewLine, document.GetPages().Select(page => page.Text));
    }

    private static bool IsRecoverablePdfParseFailure(Exception exception) =>
        exception is PdfDocumentFormatException or InvalidOperationException or IOException or ArgumentException;
}
