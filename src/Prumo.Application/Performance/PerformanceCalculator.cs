using Prumo.Domain.Enums;

namespace Prumo.Application.Performance;

public static class PerformanceCalculator
{
    public static CostBasisResult CalculateCostBasis(IEnumerable<PerformanceTransaction> source, decimal currentQuantity, decimal currentAveragePrice)
    {
        var transactions = source.OrderBy(x => x.OccurredAt).ToList();
        var reasons = new HashSet<PerformanceCoverageReason>();
        if (transactions.Count == 0)
            return new(null, null, null, null, PerformanceCoverage.Partial, [PerformanceCoverageReason.MissingOpeningPosition, PerformanceCoverageReason.MissingCostBasis]);

        if (transactions.Any(x => x.DatePrecision is FinancialDatePrecision.PeriodStart or FinancialDatePrecision.PeriodEnd or FinancialDatePrecision.Approximate))
            reasons.Add(PerformanceCoverageReason.ApproximateTransactionDate);

        decimal quantity = 0, cost = 0, realized = 0;
        foreach (var transaction in transactions)
        {
            if (transaction.Type == PortfolioTransactionType.Buy)
            {
                quantity += transaction.Quantity;
                cost += transaction.Quantity * transaction.UnitPrice;
                continue;
            }

            if (transaction.Quantity > quantity || quantity <= 0)
            {
                reasons.Add(PerformanceCoverageReason.MissingOpeningPosition);
                reasons.Add(PerformanceCoverageReason.MissingCostBasis);
                return new(null, null, null, null, PerformanceCoverage.Unavailable, reasons.ToArray());
            }

            var average = cost / quantity;
            realized += transaction.Quantity * (transaction.UnitPrice - average);
            cost -= transaction.Quantity * average;
            quantity -= transaction.Quantity;
        }

        if (quantity != currentQuantity || (quantity > 0 && Math.Abs((cost / quantity) - currentAveragePrice) > 0.01m))
            reasons.Add(PerformanceCoverageReason.IncompleteTransactionHistory);

        var coverage = reasons.Count == 0 ? PerformanceCoverage.Complete : PerformanceCoverage.Partial;
        return new(quantity, cost, quantity > 0 ? cost / quantity : 0, realized, coverage, reasons.ToArray());
    }

    public static PerformanceMetric PriceReturn(decimal? start, decimal? end, DateOnly? reliableFrom)
    {
        if (!start.HasValue || !end.HasValue || start <= 0)
            return new(null, PerformanceCoverage.Unavailable, [PerformanceCoverageReason.MissingMarketPrice]);
        return new(end.Value / start.Value - 1, PerformanceCoverage.Complete, [], reliableFrom);
    }
}
