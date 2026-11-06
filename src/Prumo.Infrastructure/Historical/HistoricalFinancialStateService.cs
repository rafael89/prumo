using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Reconciliation;

namespace Prumo.Infrastructure.Historical;

public sealed class HistoricalFinancialStateService(PrumoDbContext db, CoverageService coverage, TransactionReconciliationService reconciliation, HistoricalCashStateService? cashState = null)
{
    public async Task<HistoricalPortfolioStateDto?> Get(Guid portfolioId, DateOnly asOf, Guid? assetId, CancellationToken ct, Guid? anchorId = null)
        => await Get(portfolioId, asOf, assetId, ct, anchorId, await LoadAsync(portfolioId, asOf, ct));

    internal async Task<HistoricalPortfolioStateDto?> Get(Guid portfolioId, DateOnly asOf, Guid? assetId, CancellationToken ct, Guid? anchorId, HistoricalReadData data)
    {
        ct.ThrowIfCancellationRequested();
        if (!data.Exists) return null;
        var anchor = data.Anchors.Where(x => x.AnchorDate <= asOf && (!anchorId.HasValue || x.Id == anchorId.Value)).OrderByDescending(x => x.AnchorDate).FirstOrDefault();
        var cash = cashState is null ? null : await cashState.Get(portfolioId, asOf, ct, null, data);
        if (anchor is null) return new(portfolioId, asOf, null, "Unavailable", ["NoAnchor"], [], null, cash);
        var transactions = data.Transactions.Where(x => x.OccurredAt.Date > anchor.AnchorDate.ToDateTime(TimeOnly.MinValue) && x.OccurredAt.Date <= asOf.ToDateTime(TimeOnly.MinValue)).ToList();
        var unresolvedIds = data.Candidates.Where(x => x.TransactionA.OccurredAt.Date <= asOf.ToDateTime(TimeOnly.MinValue) && x.TransactionB.OccurredAt.Date <= asOf.ToDateTime(TimeOnly.MinValue)).SelectMany(x => new[] { x.TransactionAId, x.TransactionBId }).ToHashSet();
        var assetIds = anchor.Items.Select(x => x.AssetId).Concat(transactions.SelectMany(x => x.Items).Select(x => x.AssetId)).Distinct().Where(x => !assetId.HasValue || x == assetId).ToList();
        var assets = data.Assets;
        var corporateActions = data.Actions.Where(x => x.EffectiveDate > anchor.AnchorDate && x.EffectiveDate <= asOf).ToLookup(x => x.AssetId);
        var transactionsByAsset = transactions.SelectMany(x => x.Items.Select(i => i.AssetId).Distinct().Select(id => (id, transaction: x))).ToLookup(x => x.id, x => x.transaction);
        var anchorItems = anchor.Items.ToDictionary(x => x.AssetId);
        var result = new List<HistoricalAssetStateDto>(); var portfolioWarnings = new HashSet<string>();
        foreach (var id in assetIds)
        {
            var anchorItem = anchorItems.GetValueOrDefault(id);
            var positionCoverage = CoverageService.Resolve(data.Coverage, FinancialHistoryCoverageDimension.Position, anchor.AnchorDate, anchor.AnchorDate, id);
            decimal? quantity = anchorItem?.Quantity;
            if (quantity is null && positionCoverage.Status == "Complete") quantity = 0;
            decimal? cost = anchorItem?.AverageCost is { } average ? average * anchorItem.Quantity : null;
            if (anchorItem is not null && anchorItem.Quantity == 0) cost = 0;
            var warnings = new HashSet<string>();
            if (anchorItem is null && quantity is null) warnings.Add("UnknownOpeningState");
            var actions = corporateActions[id].ToList();
            foreach (var action in actions.Where(x => x.DatePrecision != FinancialDatePrecision.Exact)) warnings.Add("ApproximateCorporateActionDate");
            var datedTransactions = transactionsByAsset[id].OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).ToList();
            foreach (var day in actions.Where(x => x.DatePrecision == FinancialDatePrecision.Exact).Select(x => x.EffectiveDate).Concat(datedTransactions.Select(x => DateOnly.FromDateTime(x.OccurredAt.Date))).Distinct().OrderBy(x => x))
            {
                foreach (var action in actions.Where(x => x.DatePrecision == FinancialDatePrecision.Exact && x.EffectiveDate == day))
                {
                    if (quantity is not null)
                    {
                        quantity = AssetCorporateAction.Transform(quantity.Value, action.FactorNumerator, action.FactorDenominator);
                        if (cost is not null && quantity != 0) { }
                    }
                }
                foreach (var transaction in datedTransactions.Where(x => DateOnly.FromDateTime(x.OccurredAt.Date) == day))
                {
                    if (transaction.DatePrecision != nameof(FinancialDatePrecision.Exact)) warnings.Add("ApproximateTransactionDate");
                    foreach (var item in transaction.Items.Where(x => x.AssetId == id))
                    {
                        if (transaction.Type == nameof(PortfolioTransactionType.Buy))
                        {
                            if (quantity is not null) { if (cost is not null) cost += item.Quantity * item.UnitPrice; quantity += item.Quantity; }
                        }
                        else if (quantity is null || quantity < item.Quantity) { quantity = null; cost = null; warnings.Add("Oversell"); }
                        else
                        {
                            if (cost is not null) cost -= item.Quantity * (cost.Value / quantity.Value);
                            quantity -= item.Quantity; if (quantity == 0) { cost = 0; }
                        }
                    }
                }
            }
            if (unresolvedIds.Count > 0 && transactions.Any(x => x.SourceRecordIds.Any(unresolvedIds.Contains))) warnings.Add("UnresolvedTransactionReconciliation");
            var transactionCoverage = CoverageService.Resolve(data.Coverage, FinancialHistoryCoverageDimension.Transactions, anchor.AnchorDate, asOf, id);
            var corporateCoverage = CoverageService.Resolve(data.Coverage, FinancialHistoryCoverageDimension.CorporateActions, anchor.AnchorDate, asOf, id);
            if (transactionCoverage.Status != "Complete") warnings.Add("MissingTransactionCoverage");
            if (corporateCoverage.Status != "Complete") warnings.Add("MissingCorporateActionCoverage");
            var quantityCoverage = quantity is null ? "Unavailable" : transactionCoverage.Status == "Complete" && corporateCoverage.Status == "Complete" && !warnings.Contains("Oversell") && !warnings.Contains("ApproximateTransactionDate") && !warnings.Contains("ApproximateCorporateActionDate") && !warnings.Contains("UnresolvedTransactionReconciliation") ? "Complete" : "Partial";
            var costCoverage = cost is null ? "Unavailable" : quantityCoverage == "Complete" && (CoverageService.Resolve(data.Coverage, FinancialHistoryCoverageDimension.CostBasis, anchor.AnchorDate, asOf, id)).Status == "Complete" ? "Complete" : "Partial";
            var appliedActions = actions.Where(x => x.DatePrecision == FinancialDatePrecision.Exact && x.EffectiveDate <= asOf).Select(x => new AppliedCorporateActionDto(x.Id, x.AssetId, x.Type.ToString(), x.EffectiveDate, x.FactorNumerator, x.FactorDenominator)).ToList();
            result.Add(new(id, assets.GetValueOrDefault(id)?.Ticker ?? "", quantity, quantityCoverage, cost is null || quantity is null ? null : quantity == 0 ? null : cost / quantity, cost, costCoverage, warnings.ToList(), anchor.Items.Any(x => x.AssetId == id) ? [anchor.Id] : [], appliedActions));
            foreach (var warning in warnings) portfolioWarnings.Add(warning);
        }
        var current = asOf == DateOnly.FromDateTime(DateTime.UtcNow) ? CompareCurrent(result, data.Positions) : null;
        return new(portfolioId, asOf, new HistoricalAnchorReferenceDto(anchor.Id, anchor.AnchorDate, anchor.Source.ToString(), anchor.DatePrecision.ToString()), result.Any(x => x.QuantityCoverage == "Complete") ? result.All(x => x.QuantityCoverage == "Complete") ? "Complete" : "Partial" : "Unavailable", portfolioWarnings.ToList(), result, current, cash);
    }

    internal async Task<HistoricalReadData> LoadAsync(Guid portfolioId, DateOnly to, CancellationToken ct)
    {
        if (!await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct)) return new(false, [], [], [], new Dictionary<Guid, Asset>(), [], [], [], [], new Dictionary<Guid, PortfolioPosition>());
        var anchors = await db.PortfolioHistoricalAnchors.AsNoTracking().Include(x => x.Items).Where(x => x.PortfolioId == portfolioId && x.AnchorDate <= to).ToListAsync(ct);
        var transactions = await reconciliation.Canonical(portfolioId, ct);
        var candidates = await reconciliation.Candidates(portfolioId, ct);
        var ids = anchors.SelectMany(x => x.Items).Select(x => x.AssetId).Concat(transactions.SelectMany(x => x.Items).Select(x => x.AssetId)).Distinct().ToArray();
        var assets = await db.Assets.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var actions = await db.AssetCorporateActions.AsNoTracking().Where(x => ids.Contains(x.AssetId) && x.EffectiveDate <= to).OrderBy(x => x.EffectiveDate).ToListAsync(ct);
        var evidence = await coverage.List(portfolioId, ct);
        var cashAnchors = cashState is null ? [] : await db.PortfolioCashAnchors.AsNoTracking().Where(x => x.PortfolioId == portfolioId && x.AnchorDate <= to).ToListAsync(ct);
        var flows = cashState is null ? [] : await db.PortfolioCashFlows.AsNoTracking().Where(x => x.PortfolioId == portfolioId && DateOnly.FromDateTime(x.OccurredAt.UtcDateTime) <= to).OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).ToListAsync(ct);
        var positions = await db.PortfolioPositions.AsNoTracking().Where(x => x.PortfolioId == portfolioId).ToDictionaryAsync(x => x.AssetId, ct);
        return new(true, anchors, transactions, candidates, assets, actions, evidence, cashAnchors, flows, positions);
    }

    private static IReadOnlyList<CurrentQuantityComparisonDto> CompareCurrent(IReadOnlyList<HistoricalAssetStateDto> states, IReadOnlyDictionary<Guid, PortfolioPosition> current)
        => states.Select(x => new CurrentQuantityComparisonDto(x.AssetId, x.Quantity, current.GetValueOrDefault(x.AssetId)?.Quantity, x.Quantity is not null && current.GetValueOrDefault(x.AssetId)?.Quantity == x.Quantity ? "CurrentQuantityMatch" : "CurrentQuantityMismatch")).ToList();

}

public sealed record HistoricalPortfolioStateDto(Guid PortfolioId, DateOnly AsOfDate, HistoricalAnchorReferenceDto? Anchor, string Coverage, IReadOnlyList<string> Warnings, IReadOnlyList<HistoricalAssetStateDto> Assets, IReadOnlyList<CurrentQuantityComparisonDto>? CurrentComparison, HistoricalCashStateDto? Cash = null);
public sealed record HistoricalAnchorReferenceDto(Guid Id, DateOnly AnchorDate, string Source, string DatePrecision);
public sealed record HistoricalAssetStateDto(Guid AssetId, string Ticker, decimal? Quantity, string QuantityCoverage, decimal? AverageCost, decimal? TotalCostBasis, string CostBasisCoverage, IReadOnlyList<string> Warnings, IReadOnlyList<Guid> EvidenceIds, IReadOnlyList<AppliedCorporateActionDto> AppliedCorporateActions);
public sealed record AppliedCorporateActionDto(Guid Id, Guid AssetId, string Type, DateOnly EffectiveDate, decimal FactorNumerator, decimal FactorDenominator);
public sealed record CurrentQuantityComparisonDto(Guid AssetId, decimal? ReconstructedQuantity, decimal? CurrentQuantity, string Status);

internal sealed record HistoricalReadData(bool Exists, IReadOnlyList<PortfolioHistoricalAnchor> Anchors,
    IReadOnlyList<CanonicalTransactionDto> Transactions, IReadOnlyList<TransactionCandidateDto> Candidates,
    IReadOnlyDictionary<Guid, Asset> Assets, IReadOnlyList<AssetCorporateAction> Actions, IReadOnlyList<CoverageEvidenceDto> Coverage,
    IReadOnlyList<PortfolioCashAnchor> CashAnchors, IReadOnlyList<PortfolioCashFlow> CashFlows,
    IReadOnlyDictionary<Guid, PortfolioPosition> Positions);
