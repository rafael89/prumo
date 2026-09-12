using Prumo.Domain.Enums;

namespace Prumo.Application.Abstractions;

public interface IAiAnalysisService
{
    string Model { get; }
    IReadOnlyList<string> AcceptedModels => new[] { Model };
    string PromptVersion { get; }
    Task<AiAnalysisResult> AnalyzeAsync(string documentText, PortfolioAnalysisContext context, CancellationToken cancellationToken = default);
}

public sealed record AiAnalysisPayload(
    string? Importance,
    string? Classification,
    bool? RequiresAction,
    string? SuggestedAction,
    string? Summary,
    string? Impact,
    string? PortfolioImpact,
    IReadOnlyList<string>? AffectedAssets,
    decimal? Confidence,
    IReadOnlyList<AiInvestmentEventPayload>? Events = null,
    IReadOnlyList<AiFinancialFactPayload>? FinancialFacts = null);

public sealed record AiFinancialFactPayload(string? Metric, decimal? Value, string? ReferencePeriod, string? PeriodType, string? Currency, string? Unit, string? Evidence, decimal? Confidence);

public sealed record AiInvestmentEventPayload(string? EventType, DateTimeOffset? OccurredAt, string? Summary, IReadOnlyList<string>? Facts, IReadOnlyList<string>? AffectedAssets);
public sealed record AiInvestmentEventResult(InvestmentEventType EventType, DateTimeOffset? OccurredAt, string Summary, IReadOnlyList<string> Facts, IReadOnlyList<string> AffectedAssets);

public sealed record PortfolioAnalysisPositionContext(
    string Ticker,
    string AssetType,
    decimal PortfolioWeight);

public sealed record PortfolioAnalysisContext(
    Guid PortfolioId,
    string PortfolioName,
    decimal TotalPortfolioValue,
    int PositionCount,
    string Ticker,
    string AssetType,
    string? Sector,
    decimal Quantity,
    decimal AveragePrice,
    decimal? CurrentPrice,
    decimal? CurrentValue,
    decimal PortfolioWeight,
    IReadOnlyList<PortfolioAnalysisPositionContext> TopPositions,
    decimal LargestPositionWeight,
    decimal TopThreePositionWeight,
    DocumentType? DocumentType = null,
    DateTimeOffset? DocumentPublishedAt = null,
    string? DocumentSource = null,
    string? DocumentSourceCategory = null,
    string? DocumentSourceType = null,
    string? DocumentSourceSpecies = null)
{
    public const int MaxTopPositions = 5;

    public PortfolioAnalysisContext(
        string ticker,
        string assetType,
        string? sector,
        decimal quantity,
        decimal averagePrice,
        decimal? currentValue,
        decimal portfolioWeight)
        : this(Guid.Empty, string.Empty, currentValue ?? 0, 0, ticker, assetType, sector, quantity, averagePrice, null, currentValue, portfolioWeight, Array.Empty<PortfolioAnalysisPositionContext>(), portfolioWeight, portfolioWeight)
    {
    }
}

public sealed record AiAnalysisResult(
    AnalysisImportance Importance,
    AnalysisClassification Classification,
    bool RequiresAction,
    SuggestedAction SuggestedAction,
    string Summary,
    string Impact,
    string PortfolioImpact,
    IReadOnlyList<string> AffectedAssets,
    decimal Confidence,
    string RawResponse,
    IReadOnlyList<AiInvestmentEventResult>? Events = null,
    string? Model = null,
    IReadOnlyList<AiFinancialFactResult>? FinancialFacts = null,
    IReadOnlyList<AiFinancialFactRejection>? RejectedFinancialFacts = null);

public sealed record AiFinancialFactResult(FinancialFactMetric Metric, DateOnly ReferencePeriod, FinancialPeriodType PeriodType, decimal Value, string? Currency, string Unit, string Evidence, decimal Confidence);

public sealed record AiFinancialFactRejection(
    string? Metric,
    decimal? Value,
    string? Unit,
    string? Currency,
    string? ReferencePeriod,
    string? PeriodType,
    string Reason);
