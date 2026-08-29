using Lodira.Application.Abstractions;
using Lodira.Domain.Enums;

namespace Lodira.Application.Intelligence;

/// <summary>
/// Provider-independent contextual assessor. It consumes the structured event and
/// portfolio context; it does not consume the document text or legacy analysis actions.
/// </summary>
public sealed class PortfolioImpactAssessmentService : IPortfolioImpactAssessmentService
{
    public const string CurrentVersion = "portfolio-impact-v1";
    public string Version => CurrentVersion;

    public PortfolioImpactAssessmentOutput AssessPortfolioImpact(PortfolioImpactAssessmentContext context)
    {
        if (context.InvestmentEvent.Id == Guid.Empty || context.Portfolio.Id == Guid.Empty)
            throw new ArgumentException("Evento e carteira são obrigatórios.");

        var relevantWeight = context.RelevantPositions.Sum(x => x.PortfolioWeight);
        var largestWeight = context.RelevantPositions.Count == 0 ? 0 : context.RelevantPositions.Max(x => x.PortfolioWeight);
        var hasExposure = context.RelevantPositions.Count > 0 && relevantWeight > 0;

        var attention = largestWeight >= 20m ? AttentionPriority.Critical
            : largestWeight >= 10m ? AttentionPriority.High
            : hasExposure ? AttentionPriority.Medium
            : AttentionPriority.Low;

        var action = context.InvestmentEvent.EventType is InvestmentEventType.Liquidation or InvestmentEventType.CapitalRaise
            ? (hasExposure ? ActionRequirement.Review : ActionRequirement.Monitor)
            : largestWeight >= 10m ? ActionRequirement.Review
            : hasExposure ? ActionRequirement.Monitor : ActionRequirement.None;

        // Capital impact is intentionally evaluated independently from attention/action.
        // Exposure and event semantics provide context, but no dimension is copied from another.
        var capital = context.InvestmentEvent.EventType switch
        {
            InvestmentEventType.Liquidation when largestWeight >= 10m => CapitalDecisionImpact.Block,
            InvestmentEventType.CorporateAction when hasExposure => CapitalDecisionImpact.Caution,
            InvestmentEventType.CapitalRaise when hasExposure && context.Objective?.PrimaryGoal == InvestmentGoal.WealthGrowth => CapitalDecisionImpact.Caution,
            _ => CapitalDecisionImpact.None
        };

        var confidence = context.RelevantPositions.Count == 0 ? IntelligenceConfidence.Low
            : context.PortfolioHealth is null ? IntelligenceConfidence.Medium
            : IntelligenceConfidence.High;
        var exposureText = hasExposure
            ? $"A carteira possui exposição agregada de {relevantWeight:0.##}% aos ativos afetados."
            : "Não foi identificada posição atual nos ativos afetados.";
        var strategyText = context.Strategy is null ? "sem estratégia ativa" : $"estratégia v{context.Strategy.Version}";
        var objectiveText = context.Objective is null ? "sem objetivo ativo" : $"objetivo v{context.Objective.Version}";
        var behavior = action == ActionRequirement.Review
            ? "Revisar o evento no contexto da carteira antes de alterar posições."
            : action == ActionRequirement.Monitor
                ? "Acompanhar os próximos comunicados sem presumir uma alteração automática."
                : "Nenhuma ação específica necessária neste momento.";

        return new PortfolioImpactAssessmentOutput(
            attention,
            action,
            capital,
            context.InvestmentEvent.Summary,
            $"{exposureText} Avaliação contextual para {context.Portfolio.Name}, {strategyText} e {objectiveText}.",
            behavior,
            confidence);
    }
}
