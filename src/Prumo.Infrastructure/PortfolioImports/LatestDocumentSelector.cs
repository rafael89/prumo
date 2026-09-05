using Prumo.Application.Abstractions;
using Prumo.Domain.Enums;

namespace Prumo.Infrastructure.PortfolioImports;

public static class LatestDocumentSelector
{
    public static IEnumerable<DocumentCandidate> SelectIncremental(IEnumerable<DocumentCandidate> candidates)
    {
        foreach (var group in candidates.GroupBy(candidate => candidate.Ticker, StringComparer.OrdinalIgnoreCase))
        {
            var latest = group.OrderByDescending(candidate => candidate.PublishedAt)
                .ThenByDescending(candidate => candidate.SourceIdentifier ?? string.Empty, StringComparer.Ordinal).First();
            yield return latest;

            var managementReport = group.Where(candidate => candidate.DocumentType == DocumentType.ManagementReport)
                .OrderByDescending(candidate => candidate.PublishedAt)
                .ThenByDescending(candidate => candidate.SourceIdentifier ?? string.Empty, StringComparer.Ordinal).FirstOrDefault();
            if (managementReport is not null && !SameIdentity(latest, managementReport)) yield return managementReport;
        }
    }

    public static IEnumerable<DocumentCandidate> SelectPerTicker(IEnumerable<DocumentCandidate> candidates) =>
        candidates.GroupBy(candidate => candidate.Ticker, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate => candidate.PublishedAt)
                .ThenByDescending(candidate => candidate.SourceIdentifier ?? string.Empty, StringComparer.Ordinal).First());

    public static IEnumerable<DocumentCandidate> SelectBootstrap(IEnumerable<DocumentCandidate> candidates, DateTimeOffset now, int materialFactLookbackMonths)
    {
        var cutoff = now.AddMonths(-Math.Max(1, materialFactLookbackMonths));
        foreach (var group in candidates.GroupBy(candidate => candidate.Ticker, StringComparer.OrdinalIgnoreCase))
        {
            var latestReports = group.Where(candidate => IsBootstrapReport(candidate) && (candidate.DocumentType != DocumentType.RelevantFact || candidate.PublishedAt >= cutoff))
                .OrderByDescending(candidate => candidate.PublishedAt)
                .ThenByDescending(candidate => candidate.SourceIdentifier ?? string.Empty, StringComparer.Ordinal)
                .Take(2)
                .ToList();
            foreach (var report in latestReports) yield return report;

            var managementReport = group.Where(candidate => candidate.DocumentType == DocumentType.ManagementReport)
                .OrderByDescending(candidate => candidate.PublishedAt)
                .ThenByDescending(candidate => candidate.SourceIdentifier ?? string.Empty, StringComparer.Ordinal)
                .FirstOrDefault();
            if (managementReport is not null && latestReports.All(report => !SameIdentity(report, managementReport))) yield return managementReport;
        }
    }

    private static bool IsBootstrapReport(DocumentCandidate candidate) => candidate.DocumentType is DocumentType.ManagementReport or DocumentType.QuarterlyReport or DocumentType.MonthlyReport or DocumentType.RelevantFact;
    private static bool SameIdentity(DocumentCandidate? first, DocumentCandidate second) => first is not null && ((!string.IsNullOrWhiteSpace(first.SourceIdentifier) && first.SourceIdentifier == second.SourceIdentifier) || first.SourceUrl == second.SourceUrl);
}
