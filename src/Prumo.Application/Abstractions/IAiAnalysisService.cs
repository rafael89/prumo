using Prumo.Domain.Enums;

namespace Prumo.Application.Abstractions;

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

public sealed record PortfolioAnalysisContext(
    string Ticker,
    string AssetType,
    string? Sector,
    decimal Quantity,
    decimal AveragePrice,
    decimal? CurrentValue,
    decimal PortfolioWeight);

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
