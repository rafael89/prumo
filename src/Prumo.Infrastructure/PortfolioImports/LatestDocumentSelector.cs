using Prumo.Application.Abstractions;
using Prumo.Domain.Enums;

namespace Prumo.Infrastructure.PortfolioImports;

public static class LatestDocumentSelector
{
    public static IEnumerable<DocumentCandidate> SelectPerTicker(IEnumerable<DocumentCandidate> candidates) =>
        candidates.GroupBy(candidate => candidate.Ticker, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate => candidate.PublishedAt)
                .ThenByDescending(candidate => candidate.SourceIdentifier ?? string.Empty, StringComparer.Ordinal).First());

    public static IEnumerable<DocumentCandidate> SelectBootstrap(IEnumerable<DocumentCandidate> candidates, DateTimeOffset now, int materialFactLookbackMonths)
    {
        var cutoff = now.AddMonths(-Math.Max(1, materialFactLookbackMonths));
        foreach (var group in candidates.GroupBy(candidate => candidate.Ticker, StringComparer.OrdinalIgnoreCase))
        {
            var context = group.Where(IsContextDocument).OrderBy(candidate => ContextPriority(candidate.DocumentType))
                .ThenByDescending(candidate => candidate.PublishedAt)
                .ThenByDescending(candidate => candidate.SourceIdentifier ?? string.Empty, StringComparer.Ordinal).FirstOrDefault();
            if (context is not null) yield return context;

            var eventDocument = group.Where(candidate => candidate.DocumentType == DocumentType.RelevantFact && candidate.PublishedAt >= cutoff)
                .OrderByDescending(candidate => candidate.PublishedAt)
                .ThenByDescending(candidate => candidate.SourceIdentifier ?? string.Empty, StringComparer.Ordinal).FirstOrDefault();
            if (eventDocument is not null && !SameIdentity(context, eventDocument)) yield return eventDocument;
        }
    }

    private static bool IsContextDocument(DocumentCandidate candidate) => candidate.DocumentType is DocumentType.ManagementReport or DocumentType.QuarterlyReport or DocumentType.MonthlyReport;
    private static int ContextPriority(DocumentType type) => type switch
    {
        DocumentType.ManagementReport => 0,
        DocumentType.QuarterlyReport => 1,
        DocumentType.MonthlyReport => 2,
        _ => int.MaxValue
    };
    private static bool SameIdentity(DocumentCandidate? first, DocumentCandidate second) => first is not null && ((!string.IsNullOrWhiteSpace(first.SourceIdentifier) && first.SourceIdentifier == second.SourceIdentifier) || first.SourceUrl == second.SourceUrl);
}
