using Lodira.Domain.Enums;

namespace Lodira.Application.Abstractions;

public interface IDocumentSource
{
    string Name { get; }
    Task<IReadOnlyList<DocumentCandidate>> FindAsync(IReadOnlyCollection<string> tickers, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentCandidate>> FindAsync(IReadOnlyCollection<DocumentSourceAsset> assets, CancellationToken cancellationToken = default)
        => FindAsync(assets.Select(asset => asset.Ticker).ToArray(), cancellationToken);
}

public sealed record DocumentSourceAsset(string Ticker, string? Cnpj);

public sealed record DocumentCandidate(
    string Ticker,
    DocumentType DocumentType,
    DateTimeOffset PublishedAt,
    string? SourceIdentifier,
    string SourceUrl,
    string? DownloadUrl,
    string? SourceCategory = null,
    string? SourceType = null,
    string? SourceSpecies = null);
