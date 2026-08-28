using Lodira.Domain.Enums;

namespace Lodira.Application.Abstractions;

public interface IAiAnalysisService
{
    string Model { get; }
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
    decimal? Confidence);

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
    decimal TopThreePositionWeight)
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
    string RawResponse);
