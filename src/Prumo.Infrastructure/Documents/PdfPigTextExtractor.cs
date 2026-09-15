using System.ComponentModel;
using System.Diagnostics;
using Prumo.Application.Abstractions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;

namespace Prumo.Infrastructure.Documents;

public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public async Task<PdfTextExtractionResult> ExtractAsync(Stream pdf, CancellationToken cancellationToken = default)
    {
        if (pdf is null) throw new ArgumentNullException(nameof(pdf));
        if (!pdf.CanRead) return new PdfTextExtractionResult(false, string.Empty, "O arquivo PDF nao pode ser lido.");

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var buffer = new MemoryStream();
            await pdf.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();

            try
            {
                var text = ExtractText(bytes);
                cancellationToken.ThrowIfCancellationRequested();
                return new PdfTextExtractionResult(true, text, null);
            }
            catch (Exception strictException) when (IsRecoverablePdfParseFailure(strictException))
            {
                var options = new ParsingOptions
                {
                    UseLenientParsing = true,
                    SkipMissingFonts = true
                };

                try
                {
                    var text = ExtractText(bytes, options);
                    cancellationToken.ThrowIfCancellationRequested();
                    return new PdfTextExtractionResult(true, text, null);
                }
                catch (Exception lenientException) when (IsRecoverablePdfParseFailure(lenientException))
                {
                    var text = await TryExtractWithPopplerAsync(bytes, cancellationToken);
                    if (text is not null)
                        return new PdfTextExtractionResult(true, text, null);

                    return new PdfTextExtractionResult(false, string.Empty, $"Falha ao extrair texto: {strictException.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfDocumentFormatException exception)
        {
            return new PdfTextExtractionResult(false, string.Empty, $"Falha ao extrair texto: {exception.Message}");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or ArgumentException)
        {
            return new PdfTextExtractionResult(false, string.Empty, $"Falha ao extrair texto: {exception.Message}");
        }
    }

    private static string ExtractText(byte[] bytes, ParsingOptions options)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var document = PdfDocument.Open(stream, options);
        return string.Join(Environment.NewLine, document.GetPages().Select(page => page.Text));
    }

    private static string ExtractText(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var document = PdfDocument.Open(stream);
        return string.Join(Environment.NewLine, document.GetPages().Select(page => page.Text));
    }

    private static async Task<string?> TryExtractWithPopplerAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pdftotext",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("-layout");
            process.StartInfo.ArgumentList.Add("-enc");
            process.StartInfo.ArgumentList.Add("UTF-8");
            process.StartInfo.ArgumentList.Add("-");
            process.StartInfo.ArgumentList.Add("-");
            if (!process.Start()) return null;

            await process.StandardInput.BaseStream.WriteAsync(bytes, cancellationToken);
            await process.StandardInput.BaseStream.FlushAsync(cancellationToken);
            process.StandardInput.Close();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            _ = await errorTask;
            return process.ExitCode == 0 ? output : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool IsRecoverablePdfParseFailure(Exception exception) =>
        exception is PdfDocumentFormatException or InvalidOperationException or IOException or ArgumentException;
}
