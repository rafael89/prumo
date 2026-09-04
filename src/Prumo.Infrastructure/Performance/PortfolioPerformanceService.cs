using Microsoft.EntityFrameworkCore;
using Prumo.Application.Performance;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Reconciliation;

namespace Prumo.Infrastructure.Performance;

public sealed class PortfolioPerformanceService(PrumoDbContext db, TransactionReconciliationService? canonical = null)
{
    public async Task<PortfolioPerformanceDto?> GetAsync(Guid portfolioId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        if (!await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct)) return null;
        var positions = await db.PortfolioPositions.AsNoTracking().Include(x => x.Asset).Where(x => x.PortfolioId == portfolioId).ToListAsync(ct);
        var transactions = await (canonical ?? new TransactionReconciliationService(db)).Canonical(portfolioId, ct);
        var flows = await db.PortfolioCashFlows.AsNoTracking().Where(x => x.PortfolioId == portfolioId).ToListAsync(ct);
        var assetIds = positions.Select(x => x.AssetId).ToHashSet();
        var prices = await db.AssetPrices.AsNoTracking().Where(x => assetIds.Contains(x.AssetId)).OrderByDescending(x => x.PriceDate).ThenByDescending(x => x.FetchedAt).ToListAsync(ct);
        var latest = prices.GroupBy(x => x.AssetId).ToDictionary(x => x.Key, x => x.First());
        var assetResults = positions.Select(position => BuildAsset(position, transactions, prices, latest, from, to)).ToList();

        var current = MetricSum(assetResults.Select(x => x.CurrentValuation));
        var cost = MetricSum(assetResults.Select(x => x.CurrentCostBasis));
        var unrealized = MetricSum(assetResults.Select(x => x.UnrealizedPnl));
        var realized = MetricSum(assetResults.Select(x => x.RealizedPnl));
        var contributions = SumFlows(flows, PortfolioCashFlowType.Contribution, from, to);
        var withdrawals = SumFlows(flows, PortfolioCashFlowType.Withdrawal, from, to);
        var income = SumFlows(flows, PortfolioCashFlowType.Income, from, to);
        var net = new PerformanceMetric(contributions.Value.HasValue && withdrawals.Value.HasValue ? contributions.Value - withdrawals.Value : null,
            MinCoverage(contributions.Coverage, withdrawals.Coverage), Reasons: contributions.Reasons.Concat(withdrawals.Reasons).Distinct().ToArray());
        return new(current, contributions, withdrawals, net, income, cost, unrealized, realized, assetResults, from, to);
    }

    private static AssetPerformanceDto BuildAsset(PortfolioPosition position, IReadOnlyList<CanonicalTransactionDto> allTransactions, IReadOnlyList<AssetPrice> prices, IReadOnlyDictionary<Guid, AssetPrice> latest, DateOnly? from, DateOnly? to)
    {
        var tx = allTransactions.Where(x => x.Items.Any(i => i.AssetId == position.AssetId)).SelectMany(x => x.Items.Where(i => i.AssetId == position.AssetId).Select(i => new PerformanceTransaction(Enum.Parse<PortfolioTransactionType>(x.Type), i.Quantity, i.UnitPrice, x.OccurredAt, Enum.Parse<FinancialDatePrecision>(x.DatePrecision)))).ToList();
        var basis = PerformanceCalculator.CalculateCostBasis(tx, position.Quantity, position.AveragePrice);
        var valuation = latest.TryGetValue(position.AssetId, out var price)
            ? new PerformanceMetric(position.Quantity * price.Close, PerformanceCoverage.Complete, [])
            : position.Quantity == 0 ? new PerformanceMetric(0, PerformanceCoverage.Complete, []) : new PerformanceMetric(null, PerformanceCoverage.Unavailable, [PerformanceCoverageReason.MissingMarketPrice]);
        var cost = basis.CostBasis.HasValue
            ? new PerformanceMetric(basis.CostBasis, basis.Coverage, basis.Reasons)
            : new PerformanceMetric(null, basis.Coverage, basis.Reasons);
        var unrealized = valuation.Value.HasValue && cost.Value.HasValue
            ? new PerformanceMetric(valuation.Value - cost.Value, MinCoverage(valuation.Coverage, cost.Coverage), valuation.Reasons.Concat(cost.Reasons).Distinct().ToArray())
            : new PerformanceMetric(null, PerformanceCoverage.Unavailable, valuation.Reasons.Concat(cost.Reasons).Distinct().ToArray());
        var realized = basis.RealizedPnl.HasValue ? new PerformanceMetric(basis.RealizedPnl, basis.Coverage, basis.Reasons) : new PerformanceMetric(null, basis.Coverage, basis.Reasons);
        var startPrice = from.HasValue ? prices.Where(x => x.AssetId == position.AssetId && x.PriceDate <= from).OrderByDescending(x => x.PriceDate).FirstOrDefault() : null;
        var endPrice = to.HasValue ? prices.Where(x => x.AssetId == position.AssetId && x.PriceDate <= to).OrderByDescending(x => x.PriceDate).FirstOrDefault() : null;
        var priceReturn = PerformanceCalculator.PriceReturn(startPrice?.Close, endPrice?.Close, startPrice?.PriceDate);
        return new(position.AssetId, position.Asset?.Ticker ?? string.Empty, valuation, cost, unrealized, realized, priceReturn, basis.CostBasis.HasValue ? "LedgerReconstructed" : null);
    }

    private static PerformanceMetric SumFlows(IEnumerable<PortfolioCashFlow> flows, PortfolioCashFlowType type, DateOnly? from, DateOnly? to)
    {
        var selected = flows.Where(x => x.Type == type && InRange(x.OccurredAt, from, to)).ToList();
        return new(selected.Sum(x => x.Amount), PerformanceCoverage.Partial, [PerformanceCoverageReason.IncompleteCashFlowHistory]);
    }

    private static bool InRange(DateTimeOffset value, DateOnly? from, DateOnly? to) => (!from.HasValue || DateOnly.FromDateTime(value.UtcDateTime) >= from) && (!to.HasValue || DateOnly.FromDateTime(value.UtcDateTime) <= to);

    private static PerformanceMetric MetricSum(IEnumerable<PerformanceMetric> metrics)
    {
        var list = metrics.ToList();
        var known = list.Where(x => x.Value.HasValue).ToList();
        if (known.Count == 0) return new(null, PerformanceCoverage.Unavailable, list.SelectMany(x => x.Reasons).Distinct().ToArray());
        return new(known.Sum(x => x.Value!.Value), known.Count == list.Count ? MinCoverage(list.Select(x => x.Coverage).ToArray()) : PerformanceCoverage.Partial, list.SelectMany(x => x.Reasons).Distinct().ToArray());
    }

    private static PerformanceCoverage MinCoverage(params PerformanceCoverage[] values) => values.Contains(PerformanceCoverage.Unavailable) ? PerformanceCoverage.Unavailable : values.Contains(PerformanceCoverage.Partial) ? PerformanceCoverage.Partial : PerformanceCoverage.Complete;
}
