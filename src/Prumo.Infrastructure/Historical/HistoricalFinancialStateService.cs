using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Reconciliation;

namespace Prumo.Infrastructure.Historical;

public sealed class HistoricalFinancialStateService(PrumoDbContext db, CoverageService coverage, TransactionReconciliationService reconciliation, HistoricalCashStateService? cashState = null)
{
    public async Task<HistoricalPortfolioStateDto?> Get(Guid portfolioId, DateOnly asOf, Guid? assetId, CancellationToken ct, Guid? anchorId = null)
    {
        if (!await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct)) return null;
        var anchorQuery = db.PortfolioHistoricalAnchors.AsNoTracking().Include(x => x.Items).ThenInclude(x => x.Asset).Where(x => x.PortfolioId == portfolioId && x.AnchorDate <= asOf);
        if (anchorId.HasValue) anchorQuery = anchorQuery.Where(x => x.Id == anchorId.Value);
        var anchor = await anchorQuery.OrderByDescending(x => x.AnchorDate).FirstOrDefaultAsync(ct);
        if (anchor is null) return new(portfolioId, asOf, null, "Unavailable", ["NoAnchor"], [], null, cashState is null ? null : await cashState.Get(portfolioId, asOf, ct));
        var transactions = (await reconciliation.Canonical(portfolioId, ct)).Where(x => x.OccurredAt.Date > anchor.AnchorDate.ToDateTime(TimeOnly.MinValue) && x.OccurredAt.Date <= asOf.ToDateTime(TimeOnly.MinValue)).ToList();
        var candidates = await reconciliation.Candidates(portfolioId, ct);
        var unresolvedIds = candidates.Where(x => x.TransactionA.OccurredAt.Date <= asOf.ToDateTime(TimeOnly.MinValue) && x.TransactionB.OccurredAt.Date <= asOf.ToDateTime(TimeOnly.MinValue)).SelectMany(x => new[] { x.TransactionAId, x.TransactionBId }).ToHashSet();
        var assetIds = anchor.Items.Select(x => x.AssetId).Concat(transactions.SelectMany(x => x.Items).Select(x => x.AssetId)).Distinct().Where(x => !assetId.HasValue || x == assetId).ToList();
        var assets = await db.Assets.AsNoTracking().Where(x => assetIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var corporateActions = await db.AssetCorporateActions.AsNoTracking().Where(x => assetIds.Contains(x.AssetId) && x.EffectiveDate > anchor.AnchorDate && x.EffectiveDate <= asOf).OrderBy(x => x.EffectiveDate).ToListAsync(ct);
        var result = new List<HistoricalAssetStateDto>(); var portfolioWarnings = new HashSet<string>();
        foreach (var id in assetIds)
        {
            var anchorItem = anchor.Items.SingleOrDefault(x => x.AssetId == id);
            var positionCoverage = await coverage.Resolve(portfolioId, FinancialHistoryCoverageDimension.Position, anchor.AnchorDate, anchor.AnchorDate, id, ct);
            decimal? quantity = anchorItem?.Quantity;
            if (quantity is null && positionCoverage.Status == "Complete") quantity = 0;
            decimal? cost = anchorItem?.AverageCost is { } average ? average * anchorItem.Quantity : null;
            if (anchorItem is not null && anchorItem.Quantity == 0) cost = 0;
            var warnings = new HashSet<string>();
            if (anchorItem is null && quantity is null) warnings.Add("UnknownOpeningState");
            var actions = corporateActions.Where(x => x.AssetId == id).ToList();
            foreach (var action in actions.Where(x => x.DatePrecision != FinancialDatePrecision.Exact)) warnings.Add("ApproximateCorporateActionDate");
            var datedTransactions = transactions.Where(x => x.Items.Any(i => i.AssetId == id)).OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).ToList();
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
            var transactionCoverage = await coverage.Resolve(portfolioId, FinancialHistoryCoverageDimension.Transactions, anchor.AnchorDate, asOf, id, ct);
            var corporateCoverage = await coverage.Resolve(portfolioId, FinancialHistoryCoverageDimension.CorporateActions, anchor.AnchorDate, asOf, id, ct);
            if (transactionCoverage.Status != "Complete") warnings.Add("MissingTransactionCoverage");
            if (corporateCoverage.Status != "Complete") warnings.Add("MissingCorporateActionCoverage");
            var quantityCoverage = quantity is null ? "Unavailable" : transactionCoverage.Status == "Complete" && corporateCoverage.Status == "Complete" && !warnings.Contains("Oversell") && !warnings.Contains("ApproximateTransactionDate") && !warnings.Contains("ApproximateCorporateActionDate") && !warnings.Contains("UnresolvedTransactionReconciliation") ? "Complete" : "Partial";
            var costCoverage = cost is null ? "Unavailable" : quantityCoverage == "Complete" && (await coverage.Resolve(portfolioId, FinancialHistoryCoverageDimension.CostBasis, anchor.AnchorDate, asOf, id, ct)).Status == "Complete" ? "Complete" : "Partial";
            var appliedActions = actions.Where(x => x.DatePrecision == FinancialDatePrecision.Exact && x.EffectiveDate <= asOf).Select(x => new AppliedCorporateActionDto(x.Id, x.AssetId, x.Type.ToString(), x.EffectiveDate, x.FactorNumerator, x.FactorDenominator)).ToList();
            result.Add(new(id, assets.GetValueOrDefault(id)?.Ticker ?? "", quantity, quantityCoverage, cost is null || quantity is null ? null : quantity == 0 ? null : cost / quantity, cost, costCoverage, warnings.ToList(), anchor.Items.Any(x => x.AssetId == id) ? [anchor.Id] : [], appliedActions));
            foreach (var warning in warnings) portfolioWarnings.Add(warning);
        }
        var current = asOf == DateOnly.FromDateTime(DateTime.UtcNow) ? await CompareCurrent(portfolioId, result, ct) : null;
        return new(portfolioId, asOf, new HistoricalAnchorReferenceDto(anchor.Id, anchor.AnchorDate, anchor.Source.ToString(), anchor.DatePrecision.ToString()), result.Any(x => x.QuantityCoverage == "Complete") ? result.All(x => x.QuantityCoverage == "Complete") ? "Complete" : "Partial" : "Unavailable", portfolioWarnings.ToList(), result, current, cashState is null ? null : await cashState.Get(portfolioId, asOf, ct));
    }

    private async Task<IReadOnlyList<CurrentQuantityComparisonDto>> CompareCurrent(Guid portfolioId, IReadOnlyList<HistoricalAssetStateDto> states, CancellationToken ct)
    {
        var current = await db.PortfolioPositions.AsNoTracking().Where(x => x.PortfolioId == portfolioId).ToDictionaryAsync(x => x.AssetId, ct);
        return states.Select(x => new CurrentQuantityComparisonDto(x.AssetId, x.Quantity, current.GetValueOrDefault(x.AssetId)?.Quantity, x.Quantity is not null && current.GetValueOrDefault(x.AssetId)?.Quantity == x.Quantity ? "CurrentQuantityMatch" : "CurrentQuantityMismatch")).ToList();
    }
}

public sealed record HistoricalPortfolioStateDto(Guid PortfolioId, DateOnly AsOfDate, HistoricalAnchorReferenceDto? Anchor, string Coverage, IReadOnlyList<string> Warnings, IReadOnlyList<HistoricalAssetStateDto> Assets, IReadOnlyList<CurrentQuantityComparisonDto>? CurrentComparison, HistoricalCashStateDto? Cash = null);
public sealed record HistoricalAnchorReferenceDto(Guid Id, DateOnly AnchorDate, string Source, string DatePrecision);
public sealed record HistoricalAssetStateDto(Guid AssetId, string Ticker, decimal? Quantity, string QuantityCoverage, decimal? AverageCost, decimal? TotalCostBasis, string CostBasisCoverage, IReadOnlyList<string> Warnings, IReadOnlyList<Guid> EvidenceIds, IReadOnlyList<AppliedCorporateActionDto> AppliedCorporateActions);
public sealed record AppliedCorporateActionDto(Guid Id, Guid AssetId, string Type, DateOnly EffectiveDate, decimal FactorNumerator, decimal FactorDenominator);
public sealed record CurrentQuantityComparisonDto(Guid AssetId, decimal? ReconstructedQuantity, decimal? CurrentQuantity, string Status);
