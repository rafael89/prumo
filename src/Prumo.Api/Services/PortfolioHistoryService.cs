using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Reconciliation;

namespace Prumo.Api.Services;

public sealed class PortfolioHistoryService(PrumoDbContext db, TransactionReconciliationService? canonical = null)
{
    public async Task<PortfolioTimelineResponse?> GetTimelineAsync(Guid portfolioId, int limit, DateTimeOffset? before, CancellationToken ct)
    {
        var data = await LoadAsync(portfolioId, null, limit, before, ct);
        return data is null ? null : new PortfolioTimelineResponse(data.Items.Take(limit).ToList(), data.Items.Count > limit);
    }

    public async Task<AssetHistoryResponse?> GetAssetHistoryAsync(Guid portfolioId, Guid assetId, int limit, CancellationToken ct)
    {
        var data = await LoadAsync(portfolioId, assetId, limit, null, ct);
        if (data is null || !data.AssetIds.Contains(assetId)) return null;

        var asset = data.Assets.Single(x => x.Id == assetId);
        var transitions = BuildTransitions(data.Assessments, assetId);
        var purchase = data.Transactions
            .Where(x => x.Items.Any(item => item.AssetId == assetId) && x.Type == nameof(PortfolioTransactionType.Buy))
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).FirstOrDefault();
        var lastPurchaseAt = purchase?.OccurredAt;
        var sinceItems = lastPurchaseAt is null
            ? Array.Empty<PortfolioTimelineItem>()
            : data.Items.Where(x => x.AssetId == assetId && x.OccurredAt > lastPurchaseAt).ToArray();
        var current = transitions.LastOrDefault()?.CurrentAssessment;

        return new AssetHistoryResponse(
            portfolioId,
            asset.Id,
            asset.Ticker,
            asset.Name,
            data.Items.Take(limit).ToList(),
            transitions,
            new SinceLastPurchaseResponse(
                lastPurchaseAt is null ? "NoKnownPurchase" : "Available",
                lastPurchaseAt,
                sinceItems.Count(x => x.Type == "Document"),
                sinceItems.Count(x => x.Type == "InvestmentEvent"),
                sinceItems.Count(x => x.Type == "AssessmentChange"),
                current,
                sinceItems.Take(limit).ToList()));
    }

    private async Task<HistoryData?> LoadAsync(Guid portfolioId, Guid? assetId, int limit, DateTimeOffset? before, CancellationToken ct)
    {
        if (!await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct)) return null;

        var assetIdsQuery = db.PortfolioPositions.Where(x => x.PortfolioId == portfolioId).Select(x => x.AssetId);
        if (assetId.HasValue) assetIdsQuery = assetIdsQuery.Where(x => x == assetId.Value);
        var assetIds = await assetIdsQuery.ToHashSetAsync(ct);
        var assets = await db.Assets.AsNoTracking().Where(x => assetIds.Contains(x.Id)).ToListAsync(ct);
        var take = Math.Clamp(limit, 1, 200);

        var transactions = (await (canonical ?? new TransactionReconciliationService(db)).Canonical(portfolioId, ct))
            .Where(x => !assetId.HasValue || x.Items.Any(item => item.AssetId == assetId.Value))
            .Where(x => !before.HasValue || x.OccurredAt < before.Value)
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).Take(take).ToList();
        var documents = await db.Documents.AsNoTracking().Include(x => x.Asset)
            .Where(x => assetIds.Contains(x.AssetId))
            .Where(x => !before.HasValue || x.PublishedAt < before.Value)
            .OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id).Take(take).ToListAsync(ct);
        var events = await db.InvestmentEvents.AsNoTracking().AsSplitQuery().Include(x => x.AffectedAssets).Include(x => x.Evidence)
            .Include(x => x.DocumentAnalysis!).ThenInclude(x => x.Document)
            .Where(x => x.AffectedAssets.Any(asset => assetIds.Contains(asset.Id)))
            .Where(x => !before.HasValue || (x.OccurredAt.HasValue && x.OccurredAt.Value < before.Value))
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(take).ToListAsync(ct);
        var assessments = await db.PortfolioAssessments.AsNoTracking().AsSplitQuery().Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets).Include(x => x.InvestmentEvent!).ThenInclude(x => x.Evidence)
            .Where(x => x.PortfolioId == portfolioId && x.InvestmentEvent!.AffectedAssets.Any(asset => assetIds.Contains(asset.Id)))
            .Where(x => !before.HasValue || x.GeneratedAt < before.Value)
            .OrderByDescending(x => x.GeneratedAt).ThenByDescending(x => x.Id).Take(take).ToListAsync(ct);
        var guidances = await db.ContributionGuidances.AsNoTracking().Include(x => x.Allocations)
            .Where(x => x.PortfolioId == portfolioId && (!assetId.HasValue || x.Allocations.Any(item => item.AssetId == assetId.Value)))
            .Where(x => !before.HasValue || x.GeneratedAt < before.Value)
            .OrderByDescending(x => x.GeneratedAt).ThenByDescending(x => x.Id).Take(take).ToListAsync(ct);

        var items = new List<PortfolioTimelineItem>();
        foreach (var transaction in transactions)
        {
            foreach (var item in transaction.Items.Where(item => assetIds.Contains(item.AssetId)))
            {
                var asset = assets.FirstOrDefault(x => x.Id == item.AssetId);
                items.Add(new(transaction.OccurredAt, transaction.HasContributionGuidance ? "ContributionExecuted" : "Transaction", asset?.Id, asset?.Ticker,
                    transaction.HasContributionGuidance ? "Aporte realizado" : "Compra registrada",
                    $"{item.Quantity:0.####} unidade(s) a {item.UnitPrice:C}.", "PortfolioTransaction", transaction.Id, null, null, null));
            }
        }
        items.AddRange(documents.Select(document => new PortfolioTimelineItem(document.PublishedAt, "Document", document.AssetId, document.Asset?.Ticker, "Documento publicado", document.SourceType ?? document.SourceCategory ?? document.SourceName, "Document", document.Id, document.Id, null, null)));
        items.AddRange(events.Select(item => new PortfolioTimelineItem(item.OccurredAt, "InvestmentEvent", item.AffectedAssets.FirstOrDefault(asset => assetIds.Contains(asset.Id))?.Id, item.AffectedAssets.FirstOrDefault(asset => assetIds.Contains(asset.Id))?.Ticker, "Evento identificado", item.Summary, "InvestmentEvent", item.Id, item.Evidence.FirstOrDefault()?.DocumentId, null, null)));
        foreach (var guidance in guidances)
        {
            foreach (var allocation in guidance.Allocations.Where(x => x.AssetId.HasValue && assetIds.Contains(x.AssetId.Value)))
            {
                var asset = assets.FirstOrDefault(x => x.Id == allocation.AssetId!.Value);
                items.Add(new(guidance.GeneratedAt, "ContributionGuidance", asset?.Id, asset?.Ticker, "Orientação de aporte", allocation.Reason, "ContributionGuidance", guidance.Id, null, null, null));
            }
        }
        items.AddRange(BuildTransitions(assessments, assetId).Select(x => new PortfolioTimelineItem(x.OccurredAt, x.PreviousAssessment is null ? "AssessmentInitial" : "AssessmentChange", x.AssetId, x.Ticker, x.PreviousAssessment is null ? "Avaliação inicial" : "Avaliação alterada", x.Reason, "PortfolioAssessment", x.AssessmentId, x.DocumentId, x.PreviousAssessment, x.CurrentAssessment)));

        return new HistoryData(assetIds, assets, transactions, assessments, items.OrderByDescending(x => x.OccurredAt.HasValue).ThenByDescending(x => x.OccurredAt).ThenByDescending(x => x.RelatedEntityId).ToList());
    }

    private static IReadOnlyList<AssessmentTransition> BuildTransitions(IEnumerable<PortfolioAssessment> assessments, Guid? assetId)
    {
        var relevant = assessments
            .SelectMany(assessment => assessment.InvestmentEvent!.AffectedAssets.Where(asset => !assetId.HasValue || asset.Id == assetId.Value).Select(asset => (assessment, asset)))
            .GroupBy(x => x.asset.Id)
            .SelectMany(group =>
            {
                string? previous = null;
                return group.OrderBy(x => x.assessment.GeneratedAt).ThenBy(x => x.assessment.Id).Select(x =>
                {
                    var current = x.assessment.CapitalDecisionImpact.ToString();
                    var transition = previous is null || !string.Equals(previous, current, StringComparison.Ordinal)
                        ? new AssessmentTransition(x.assessment.GeneratedAt, x.asset.Id, x.asset.Ticker, previous, current, x.assessment.InvestmentEventId, x.assessment.Id, x.assessment.InvestmentEvent?.Evidence.FirstOrDefault()?.DocumentId, x.assessment.InvestmentEvent?.Summary ?? x.assessment.Summary)
                        : null;
                    previous = current;
                    return transition;
                }).Where(x => x is not null).Cast<AssessmentTransition>();
            })
            .OrderBy(x => x.OccurredAt).ThenBy(x => x.AssessmentId)
            .ToList();
        return relevant;
    }

    private sealed record HistoryData(HashSet<Guid> AssetIds, IReadOnlyList<Asset> Assets, IReadOnlyList<CanonicalTransactionDto> Transactions, IReadOnlyList<PortfolioAssessment> Assessments, IReadOnlyList<PortfolioTimelineItem> Items);
}

public sealed record PortfolioTimelineResponse(IReadOnlyList<PortfolioTimelineItem> Items, bool HasMore);
public sealed record PortfolioTimelineItem(DateTimeOffset? OccurredAt, string Type, Guid? AssetId, string? Ticker, string Title, string Summary, string Source, Guid? RelatedEntityId, Guid? RelatedDocumentId, string? PreviousAssessment, string? CurrentAssessment);
public sealed record AssessmentTransition(DateTimeOffset OccurredAt, Guid AssetId, string Ticker, string? PreviousAssessment, string CurrentAssessment, Guid InvestmentEventId, Guid AssessmentId, Guid? DocumentId, string Reason);
public sealed record AssetHistoryResponse(Guid PortfolioId, Guid AssetId, string Ticker, string Name, IReadOnlyList<PortfolioTimelineItem> Items, IReadOnlyList<AssessmentTransition> AssessmentEvolution, SinceLastPurchaseResponse SinceLastPurchase);
public sealed record SinceLastPurchaseResponse(string Status, DateTimeOffset? LastPurchaseAt, int DocumentsCount, int EventsCount, int AssessmentChanges, string? CurrentAssessment, IReadOnlyList<PortfolioTimelineItem> Items);
