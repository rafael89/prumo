namespace Lodira.Application.Abstractions;

public interface IPdfTextExtractor
{
    Task<PdfTextExtractionResult> ExtractAsync(Stream pdf, CancellationToken cancellationToken = default);
}

public sealed record PdfTextExtractionResult(bool Succeeded, string Text, string? Error);
