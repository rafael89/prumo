using Lodira.Domain.Enums;

namespace Lodira.Application.Abstractions;

public interface IDocumentSource
{
    string Name { get; }
    Task<IReadOnlyList<DocumentCandidate>> FindAsync(IReadOnlyCollection<string> tickers, CancellationToken cancellationToken = default);
}

public sealed record DocumentCandidate(
    string Ticker,
    DocumentType DocumentType,
    DateTimeOffset PublishedAt,
    string? SourceIdentifier,
    string SourceUrl,
    string? DownloadUrl);
