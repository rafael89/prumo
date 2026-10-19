using Prumo.Application.Abstractions;
using Prumo.Domain.Enums;
using Prumo.Application.Radar;

namespace Prumo.Application.Intelligence;

public sealed class PortfolioImpactAssessmentService : IPortfolioImpactAssessmentService
{
    // v5 reaplica a regra percentual com os limites balanceados de 3%, 5% e 10%.
    // v4 usou a regra de existência de posição e não deve ser reutilizada.
    public const string CurrentVersion = "portfolio-impact-v8";
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
        var analysis = context.InvestmentEvent.DocumentAnalysis;
        var details = context.InvestmentEvent.RadarDetails;
        var importance = RadarEventPolicy.Importance(context.InvestmentEvent, context.AnalysisImportance);
        var basis = details is null ? analysis?.AttentionBasis : details.AttentionBasis;
        var attentionReason = details is null ? analysis?.AttentionReason : details.AttentionReason;
        var attentionEvidence = details is null ? analysis?.AttentionEvidence : details.AttentionEvidence;
        var hasAttentionEvidence = (basis is "CHANGE" or "DEADLINE" or "DECISION")
            && !string.IsNullOrWhiteSpace(attentionReason)
            && !string.IsNullOrWhiteSpace(attentionEvidence)
            && details?.Novelty != "Repeat";
        var usesMaterialEventThreshold = context.InvestmentEvent.EventType is InvestmentEventType.CorporateAction;
        var isInformational = details is not null ? importance is AnalysisImportance.Informative or AnalysisImportance.NoAction : context.AnalysisImportance == AnalysisImportance.Informative
            && context.RequiresAction == false
            && context.SuggestedAction == SuggestedAction.NoAction;
        var explicitlyNoAction = details is not null ? isInformational : context.RequiresAction == false
            && context.SuggestedAction is SuggestedAction.Maintain or SuggestedAction.NoAction;

        var capitalImpact = isInformational ? CapitalDecisionImpact.None : context.InvestmentEvent.EventType switch
        {
            InvestmentEventType.Liquidation when largestExposure >= HighExposureThreshold => CapitalDecisionImpact.Block,
            InvestmentEventType.CorporateAction when hasExposure && largestExposure >= HighExposureThreshold => CapitalDecisionImpact.Caution,
            InvestmentEventType.CapitalRaise when hasExposure && largestExposure >= HighExposureThreshold => CapitalDecisionImpact.Caution,
            _ => CapitalDecisionImpact.None
        };

        // A portfolio-specific caution or block needs review even when the
        // document's general suggestion is to maintain the position.
        var action = capitalImpact is CapitalDecisionImpact.Caution or CapitalDecisionImpact.Block ? ActionRequirement.Review
            : isInformational || explicitlyNoAction ? ActionRequirement.None
            : (details is null ? context.RequiresAction == true : hasAttentionEvidence) && hasExposure ? ActionRequirement.Review
            : context.InvestmentEvent.EventType is InvestmentEventType.Liquidation or InvestmentEventType.CapitalRaise
            ? hasExposure && largestExposure >= HighExposureThreshold ? ActionRequirement.Review : ActionRequirement.Monitor
            : largestExposure >= HighExposureThreshold ? ActionRequirement.Review
            : hasExposure && largestExposure > MediumExposureThreshold ? ActionRequirement.Monitor
            : ActionRequirement.None;

        // Exposure describes portfolio sensitivity; it cannot establish novelty.
        if (!hasAttentionEvidence)
        {
            capitalImpact = CapitalDecisionImpact.None;
            action = !isInformational && !explicitlyNoAction && hasExposure ? ActionRequirement.Monitor : ActionRequirement.None;
        }

        var attentionExposureThreshold = usesMaterialEventThreshold ? HighExposureThreshold : MediumExposureThreshold;
        var attention = isInformational || details?.Novelty == "Repeat" ? AttentionPriority.Low
            : largestExposure >= CriticalExposureThreshold ? AttentionPriority.Critical
            : largestExposure >= HighExposureThreshold ? AttentionPriority.High
            : largestExposure > attentionExposureThreshold || action is ActionRequirement.Review or ActionRequirement.Act || capitalImpact >= CapitalDecisionImpact.Caution
                ? AttentionPriority.Medium
                : AttentionPriority.Low;

        var confidence = context.RelevantPositions.Count == 0 ? IntelligenceConfidence.Low
            : context.PortfolioHealth is null ? IntelligenceConfidence.Medium : IntelligenceConfidence.High;
        var reason = details?.Novelty == "Repeat" ? "Continuidade de acontecimento já conhecido, sem mudança econômica comprovada; sem novo alerta forte."
            : hasAttentionEvidence
            ? $"Motivo da revisão: {attentionReason} Evidência no documento: {attentionEvidence}"
            : "Sem mudança material, prazo ou decisão comprovados nesta análise. A novidade do risco não foi estabelecida; não há urgência demonstrada.";
        var exposureText = hasExposure
            ? $"{reason} Exposição aos ativos afetados: {totalExposure:0.##}%. Esse peso não representa uma estimativa de perda."
            : $"{reason} Não foi identificada posição atual nos ativos afetados.";
        var behavior = capitalImpact == CapitalDecisionImpact.Block
            ? "Evitar aumentar exposição e revisar o evento antes de alterar posições."
            : action == ActionRequirement.Review
            ? "Revisar o evento no contexto da carteira antes de alterar posições."
            : action == ActionRequirement.Monitor
                ? "Acompanhar os próximos comunicados sem presumir uma alteração automática."
                : "Nenhuma ação específica necessária neste momento.";

        return new(attention, action, capitalImpact, context.InvestmentEvent.Summary,
            exposureText, behavior, confidence);
    }
}
