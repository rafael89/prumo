using Lodira.Application.Portfolio;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using DomainPortfolio = Lodira.Domain.Entities.Portfolio;

namespace Lodira.Application.Abstractions;

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
    PortfolioHealthResult? PortfolioHealth);

public sealed record PortfolioImpactAssessmentOutput(
    AttentionPriority AttentionPriority,
    ActionRequirement ActionRequirement,
    CapitalDecisionImpact CapitalDecisionImpact,
    string Summary,
    string WhyItMatters,
    string SuggestedBehavior,
    IntelligenceConfidence Confidence);
