using Prumo.Domain.Enums;

namespace Prumo.Application.Performance;

public enum PerformanceCoverage { Complete, Partial, Unavailable }

public enum PerformanceCoverageReason
{
    MissingMarketPrice,
    MissingOpeningPosition,
    IncompleteTransactionHistory,
    MissingCostBasis,
    ApproximateTransactionDate,
    PossibleDuplicateTransaction,
    UnsupportedCorporateAction,
    IncompleteCashFlowHistory
}

public sealed record PerformanceMetric(decimal? Value, PerformanceCoverage Coverage, IReadOnlyList<PerformanceCoverageReason> Reasons, DateOnly? ReliableFrom = null);

public sealed record AssetPerformanceDto(
    Guid AssetId,
    string Ticker,
    PerformanceMetric CurrentValuation,
    PerformanceMetric CurrentCostBasis,
    PerformanceMetric UnrealizedPnl,
    PerformanceMetric RealizedPnl,
    PerformanceMetric PriceReturn,
    string? CostBasisSource);

public sealed record PortfolioPerformanceDto(
    PerformanceMetric CurrentValuation,
    PerformanceMetric KnownContributions,
    PerformanceMetric KnownWithdrawals,
    PerformanceMetric KnownNetContributions,
    PerformanceMetric KnownIncome,
    PerformanceMetric CurrentCostBasis,
    PerformanceMetric UnrealizedPnl,
    PerformanceMetric RealizedPnl,
    IReadOnlyList<AssetPerformanceDto> Assets,
    DateOnly? From,
    DateOnly? To);

public sealed record PerformanceTransaction(
    PortfolioTransactionType Type,
    decimal Quantity,
    decimal UnitPrice,
    DateTimeOffset OccurredAt,
    FinancialDatePrecision DatePrecision);

public sealed record CostBasisResult(
    decimal? Quantity,
    decimal? CostBasis,
    decimal? AverageCost,
    decimal? RealizedPnl,
    PerformanceCoverage Coverage,
    IReadOnlyList<PerformanceCoverageReason> Reasons);
