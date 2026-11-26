using Microsoft.EntityFrameworkCore;
using Prumo.Application.Abstractions;
using Prumo.Application.Intelligence;
using Prumo.Application.Portfolio;
using Prumo.Application.Radar;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Api.Services;

internal sealed class PortfolioAssessmentReader(PrumoDbContext db, PortfolioAssessmentCurrentService currentAssessmentService, IPortfolioAssessmentFreshnessEvaluator freshnessEvaluator)
{
    public async Task<List<PortfolioAssessmentReadRow>> ReadAsync(IQueryable<PortfolioAssessment> query, Portfolio portfolio, IReadOnlyList<TargetAllocation> targets, CancellationToken ct)
    {
        var portfolioId = portfolio.Id;
        var snapshot = PortfolioSnapshotKeyCalculator.Calculate(portfolio);
        var targetKey = TargetAllocationKeyCalculator.Calculate(targets);
        // Read only selection metadata first. Analysis responses, evidence and assets
        // are hydrated for the requested page, or when an event actually needs refresh.
        async Task<List<PortfolioAssessmentReadRow>> ReadRows() => (await query.Select(x => new PortfolioAssessmentReadRow(
            x, x.InvestmentEvent!, x.InvestmentEvent!.DocumentAnalysis!.Importance,
            x.InvestmentEvent.DocumentAnalysis.Document!.PublishedAt,
            x.InvestmentEvent.RadarDetails == null && x.InvestmentEvent.EventType == InvestmentEventType.Liquidation
                && x.InvestmentEvent.AffectedAssets.Any(a => db.PortfolioPositions.Any(p => p.PortfolioId == portfolioId && p.AssetId == a.Id)))
            {
                IsEligible = RadarEligibilityPolicy.IsEligible(x.InvestmentEvent.EventType, x.InvestmentEvent.DocumentAnalysis.Classification,
                    x.InvestmentEvent.DocumentAnalysis.Document.SourceCategory,
                    x.InvestmentEvent.DocumentAnalysis.Document.SourceType,
                    x.InvestmentEvent.DocumentAnalysis.Document.SourceSpecies, x.InvestmentEvent.RadarDetails)
            })
            .ToListAsync(ct)).Where(x => x.IsEligible).ToList();
        bool Fresh(PortfolioAssessmentReadRow row) => freshnessEvaluator.Evaluate(row.Assessment, snapshot, targetKey, row.Event, PortfolioImpactAssessmentService.CurrentVersion).Status == FreshnessStatus.Fresh;
        var rows = await ReadRows();
        foreach (var row in rows) row.Assessment.AttachInvestmentEvent(row.Event);
        var latestEvents = RadarEventPolicy.Latest(rows.Select(x => x.Assessment)).Select(x => x.InvestmentEventId).ToHashSet();
        rows = rows.Where(x => latestEvents.Contains(x.Event.Id)).ToList();
        var staleCards = rows.GroupBy(x => x.Event.Id)
            .Where(group => group.Any(x => x.Assessment.Status != AssessmentStatus.Closed) && !group.Any(Fresh))
            .SelectMany(group => group.Select(x => RadarEventPolicy.CardId(x.Event))).ToHashSet();
        if (staleCards.Count > 0)
        {
            var refreshIds = rows.Where(x => staleCards.Contains(RadarEventPolicy.CardId(x.Event))).Select(x => x.Assessment.Id).ToArray();
            var stale = await LoadDetailsAsync(refreshIds, ct);
            await currentAssessmentService.EnsureFreshAsync(portfolio, targets, stale, ct);
            rows = await ReadRows();
            foreach (var row in rows) row.Assessment.AttachInvestmentEvent(row.Event);
            latestEvents = RadarEventPolicy.Latest(rows.Select(x => x.Assessment)).Select(x => x.InvestmentEventId).ToHashSet();
            rows = rows.Where(x => latestEvents.Contains(x.Event.Id)).ToList();
        }
        rows = rows.Where(x => x.Assessment.Status == AssessmentStatus.Closed || Fresh(x)).ToList();
        foreach (var row in rows.Where(x => x.LegacyLiquidation)) row.Assessment.OverrideCapitalDecisionImpact(CapitalDecisionImpact.Block);
        var cards = await RadarCardReader.ReadAsync(db, portfolioId, ct);
        return rows.Select(x => x with { CardId = cards.GetValueOrDefault(x.Event.DocumentAnalysisId, x.Event.DocumentAnalysisId) }).ToList();
    }

    public Task<List<PortfolioAssessment>> LoadDetailsAsync(Guid[] ids, CancellationToken ct)
        => db.PortfolioAssessments.AsNoTracking().AsSplitQuery()
            .Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets)
            .Include(x => x.InvestmentEvent!).ThenInclude(x => x.Evidence)
            .Include(x => x.InvestmentEvent!).ThenInclude(x => x.DocumentAnalysis!).ThenInclude(x => x.Document)
            .Where(x => ids.Contains(x.Id)).ToListAsync(ct);

}

internal sealed record PortfolioAssessmentReadRow(PortfolioAssessment Assessment, InvestmentEvent Event, AnalysisImportance Importance, DateTimeOffset PublishedAt, bool LegacyLiquidation)
{
    public Guid CardId { get; init; }
    public bool IsEligible { get; init; }
}
