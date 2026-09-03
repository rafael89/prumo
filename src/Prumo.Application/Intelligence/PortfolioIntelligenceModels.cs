namespace Prumo.Application.Intelligence;

public enum IntelligenceSignalType
{
    AllocationDeviation,
    HighAttentionRadar,
    AssessmentImproved,
    AssessmentDeteriorated,
    RecentRelevantEvent,
    PriceIncrease,
    PriceDecrease,
    DecisionFollowUp,
    PerformanceObservation,
    CoverageWarning
}

public enum IntelligenceSignalQuality { Safe, Qualified, Unsafe }

public sealed record IntelligenceEvidence(string Type, string Id, string Label);

public sealed record PortfolioIntelligenceSignal(
    string Id,
    IntelligenceSignalType Type,
    int Priority,
    string Title,
    string Description,
    Guid? AssetId,
    string? Ticker,
    IntelligenceSignalQuality Quality,
    IReadOnlyList<IntelligenceEvidence> Evidence);

public sealed record PortfolioIntelligenceDto(
    DateTimeOffset GeneratedAt,
    string Summary,
    IReadOnlyList<PortfolioIntelligenceSignal> AttentionItems,
    IReadOnlyList<PortfolioIntelligenceSignal> PriceMovers,
    IReadOnlyList<PortfolioIntelligenceSignal> AllocationInsights,
    IReadOnlyList<PortfolioIntelligenceSignal> DecisionReviews,
    IReadOnlyList<PortfolioIntelligenceSignal> AssetInsights,
    IReadOnlyList<PortfolioIntelligenceSignal> CoverageWarnings);
