using Prumo.Application.Abstractions;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.PortfolioImports;

namespace Prumo.Domain.Tests;

public sealed class LatestDocumentSelectorTests
{
    [Fact]
    public void Incremental_keeps_latest_and_latest_management_report_per_ticker()
    {
        var candidates = new[]
        {
            Candidate("AAA3", "A", 10),
            Candidate("AAA3", "B", 11),
            Candidate("AAA3", "C", 12),
            Candidate("AAA3", "D", 9, DocumentType.ManagementReport),
            Candidate("AAA3", "E", 8, DocumentType.ManagementReport)
        };

        var selected = LatestDocumentSelector.SelectIncremental(candidates).ToArray();

        Assert.Equal(new[] { "C", "D" }, selected.Select(x => x.SourceIdentifier));
    }

    [Fact]
    public void Bootstrap_keeps_its_limited_context_policy()
    {
        var candidates = new[]
        {
            Candidate("AAA3", "A", 10, DocumentType.ManagementReport),
            Candidate("AAA3", "B", 11, DocumentType.RelevantFact),
            Candidate("AAA3", "C", 12, DocumentType.RelevantFact)
        };

        var selected = LatestDocumentSelector.SelectBootstrap(candidates, DateTimeOffset.UtcNow, 12).ToArray();

        Assert.Equal(new[] { "A", "C" }, selected.Select(x => x.SourceIdentifier));
    }

    private static DocumentCandidate Candidate(string ticker, string identifier, int day, DocumentType type = DocumentType.RelevantFact) =>
        new(ticker, type, new DateTimeOffset(2026, 8, day, 0, 0, 0, TimeSpan.Zero), identifier, $"https://source/{identifier}", $"https://download/{identifier}");
}
