using Prumo.Application.Abstractions;
using Prumo.Domain.Enums;

namespace Prumo.Application.Intelligence;

public sealed class PortfolioImpactAssessmentService : IPortfolioImpactAssessmentService
{
    // v5 reaplica a regra percentual com os limites balanceados de 3%, 5% e 10%.
    // v4 usou a regra de existência de posição e não deve ser reutilizada.
    public const string CurrentVersion = "portfolio-impact-v6";
    private const decimal MediumExposureThreshold = 3m;
    private const decimal HighExposureThreshold = 5m;
    private const decimal CriticalExposureThreshold = 10m;

    public string Version => CurrentVersion;

    public PortfolioImpactAssessmentOutput AssessPortfolioImpact(PortfolioImpactAssessmentContext context)
    {
        if (context.InvestmentEvent.Id == Guid.Empty || context.Portfolio.Id == Guid.Empty)
            throw new ArgumentException("Evento e carteira são obrigatórios.");

        var totalExposure = context.RelevantPositions.Sum(x => x.PortfolioWeight);
        var largestExposure = context.RelevantPositions.Count == 0 ? 0 : context.RelevantPositions.Max(x => x.PortfolioWeight);
        var hasExposure = context.RelevantPositions.Count > 0 && totalExposure > 0;
        var usesMaterialEventThreshold = context.InvestmentEvent.EventType is InvestmentEventType.CorporateAction;
        var isInformational = context.AnalysisImportance == AnalysisImportance.Informative
            && context.RequiresAction == false
            && context.SuggestedAction == SuggestedAction.NoAction;

        var action = isInformational ? ActionRequirement.None
            : context.RequiresAction == true && hasExposure ? ActionRequirement.Review
            : context.InvestmentEvent.EventType is InvestmentEventType.Liquidation or InvestmentEventType.CapitalRaise
            ? hasExposure && largestExposure >= HighExposureThreshold ? ActionRequirement.Review : ActionRequirement.Monitor
            : largestExposure >= HighExposureThreshold ? ActionRequirement.Review
            : hasExposure && largestExposure > MediumExposureThreshold ? ActionRequirement.Monitor
            : ActionRequirement.None;

        var capitalImpact = isInformational ? CapitalDecisionImpact.None : context.InvestmentEvent.EventType switch
        {
            InvestmentEventType.Liquidation when largestExposure >= HighExposureThreshold => CapitalDecisionImpact.Block,
            InvestmentEventType.CorporateAction when hasExposure && largestExposure >= HighExposureThreshold => CapitalDecisionImpact.Caution,
            InvestmentEventType.CapitalRaise when hasExposure && largestExposure >= HighExposureThreshold => CapitalDecisionImpact.Caution,
            _ => CapitalDecisionImpact.None
        };

        var attentionExposureThreshold = usesMaterialEventThreshold ? HighExposureThreshold : MediumExposureThreshold;
        var attention = isInformational ? AttentionPriority.Low
            : largestExposure >= CriticalExposureThreshold ? AttentionPriority.Critical
            : largestExposure >= HighExposureThreshold ? AttentionPriority.High
            : largestExposure > attentionExposureThreshold || action is ActionRequirement.Review or ActionRequirement.Act || capitalImpact >= CapitalDecisionImpact.Caution
                ? AttentionPriority.Medium
                : AttentionPriority.Low;

        var confidence = context.RelevantPositions.Count == 0 ? IntelligenceConfidence.Low
            : context.PortfolioHealth is null ? IntelligenceConfidence.Medium : IntelligenceConfidence.High;
        var exposureText = hasExposure
            ? $"Exposição aos ativos afetados: {totalExposure:0.##}%. Impacto potencial perceptível."
            : "Não foi identificada posição atual nos ativos afetados.";
        var behavior = action == ActionRequirement.Review
            ? "Revisar o evento no contexto da carteira antes de alterar posições."
            : action == ActionRequirement.Monitor
                ? "Acompanhar os próximos comunicados sem presumir uma alteração automática."
                : "Nenhuma ação específica necessária neste momento.";

        return new(attention, action, capitalImpact, context.InvestmentEvent.Summary,
            exposureText, behavior, confidence);
    }
}
