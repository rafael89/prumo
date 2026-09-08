using Prumo.Application.Abstractions;
using Prumo.Domain.Enums;

namespace Prumo.Application.Intelligence;

public sealed class PortfolioImpactAssessmentService : IPortfolioImpactAssessmentService
{
    public const string CurrentVersion = "portfolio-impact-v4";

    public string Version => CurrentVersion;

    public PortfolioImpactAssessmentOutput AssessPortfolioImpact(PortfolioImpactAssessmentContext context)
    {
        if (context.InvestmentEvent.Id == Guid.Empty || context.Portfolio.Id == Guid.Empty)
            throw new ArgumentException("Evento e carteira são obrigatórios.");

        var hasAffectedPosition = context.RelevantPositions.Count > 0;
        var criticalDocument = context.InvestmentEvent.DocumentAnalysis?.Importance == AnalysisImportance.Critical;

        var materialEvent = context.InvestmentEvent.EventType is InvestmentEventType.Liquidation
            or InvestmentEventType.CapitalRaise
            or InvestmentEventType.CorporateAction;
        var action = hasAffectedPosition && (criticalDocument || materialEvent)
            ? ActionRequirement.Review
            : hasAffectedPosition && context.InvestmentEvent.DocumentAnalysis?.Importance == AnalysisImportance.Relevant
                ? ActionRequirement.Monitor
                : materialEvent && !hasAffectedPosition ? ActionRequirement.Monitor : ActionRequirement.None;

        var capitalImpact = context.InvestmentEvent.EventType switch
        {
            InvestmentEventType.Liquidation when hasAffectedPosition => CapitalDecisionImpact.Block,
            InvestmentEventType.CorporateAction when hasAffectedPosition => CapitalDecisionImpact.Caution,
            InvestmentEventType.CapitalRaise when hasAffectedPosition => CapitalDecisionImpact.Caution,
            _ when criticalDocument && hasAffectedPosition => CapitalDecisionImpact.Caution,
            _ => CapitalDecisionImpact.None
        };

        var attention = criticalDocument || capitalImpact == CapitalDecisionImpact.Block ? AttentionPriority.Critical
            : capitalImpact == CapitalDecisionImpact.Caution || action is ActionRequirement.Review or ActionRequirement.Act
                ? AttentionPriority.Medium
                : AttentionPriority.Low;

        var confidence = context.RelevantPositions.Count == 0 ? IntelligenceConfidence.Low
            : context.PortfolioHealth is null ? IntelligenceConfidence.Medium : IntelligenceConfidence.High;
        var portfolioText = hasAffectedPosition
            ? "A carteira possui posição em pelo menos um dos ativos afetados."
            : "Não foi identificada posição atual nos ativos afetados.";
        var behavior = action == ActionRequirement.Review
            ? "Revisar o evento no contexto da carteira antes de alterar posições."
            : action == ActionRequirement.Monitor
                ? "Acompanhar os próximos comunicados sem presumir uma alteração automática."
                : "Nenhuma ação específica necessária neste momento.";

        return new(attention, action, capitalImpact, context.InvestmentEvent.Summary,
            $"{portfolioText} Avaliação contextual para {context.Portfolio.Name}.", behavior, confidence);
    }
}
