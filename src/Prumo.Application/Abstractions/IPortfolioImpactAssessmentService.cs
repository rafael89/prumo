using Prumo.Application.Portfolio;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using DomainPortfolio = Prumo.Domain.Entities.Portfolio;

namespace Prumo.Application.Abstractions;

public interface IPortfolioImpactAssessmentService
{
    string Version { get; }

    PortfolioImpactAssessmentOutput AssessPortfolioImpact(PortfolioImpactAssessmentContext context);
}

public sealed record RelevantPositionContext(
    Guid AssetId,
    string Ticker,
    AssetClass AssetClass,
    decimal CurrentValue,
    decimal PortfolioWeight);

public sealed record PortfolioImpactAssessmentContext(
    InvestmentEvent InvestmentEvent,
    DomainPortfolio Portfolio,
    IReadOnlyList<RelevantPositionContext> RelevantPositions,
    PortfolioHealthResult? PortfolioHealth,
    AnalysisImportance? AnalysisImportance = null,
    bool? RequiresAction = null,
    SuggestedAction? SuggestedAction = null);

public sealed record PortfolioImpactAssessmentOutput(
    AttentionPriority AttentionPriority,
    ActionRequirement ActionRequirement,
    CapitalDecisionImpact CapitalDecisionImpact,
    string Summary,
    string WhyItMatters,
    string SuggestedBehavior,
    IntelligenceConfidence Confidence);
