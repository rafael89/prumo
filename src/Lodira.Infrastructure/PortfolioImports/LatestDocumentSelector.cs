using Lodira.Application.Abstractions;

namespace Lodira.Infrastructure.PortfolioImports;

public static class LatestDocumentSelector
{
    public static IEnumerable<DocumentCandidate> SelectPerTicker(IEnumerable<DocumentCandidate> candidates) =>
        candidates
            .GroupBy(candidate => candidate.Ticker, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(candidate => candidate.PublishedAt)
                .ThenByDescending(candidate => candidate.SourceIdentifier ?? string.Empty, StringComparer.Ordinal)
                .First());
}
