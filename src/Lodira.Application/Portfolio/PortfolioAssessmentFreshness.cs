using Lodira.Application.Abstractions;
using Lodira.Domain.Entities;

namespace Lodira.Application.Portfolio;

public enum FreshnessStatus { Fresh, Stale, Unknown }
public enum FreshnessDependencyType { PortfolioSnapshot, Strategy, Objective, InvestmentEvent, AssessmentPolicy, MissingMetadata }
public sealed record FreshnessReason(FreshnessDependencyType DependencyType, string? ExpectedVersionOrKey, string? CurrentVersionOrKey, string Reason);
public sealed record FreshnessResult(FreshnessStatus Status, IReadOnlyList<FreshnessReason> Reasons)
{
    public static FreshnessResult Fresh() => new(FreshnessStatus.Fresh, Array.Empty<FreshnessReason>());
}

public sealed class PortfolioAssessmentFreshnessEvaluator : IPortfolioAssessmentFreshnessEvaluator
{
    public FreshnessResult Evaluate(PortfolioAssessment assessment, string? currentPortfolioSnapshotKey, int? currentStrategyVersion, int? currentObjectiveVersion, InvestmentEvent? currentEvent, string currentAssessmentPolicyVersion)
    {
        var reasons = new List<FreshnessReason>();
        if (string.IsNullOrWhiteSpace(assessment.PortfolioSnapshotKey) || !assessment.InvestmentEventVersion.HasValue)
        {
            reasons.Add(new(FreshnessDependencyType.MissingMetadata, null, null, "A avaliação foi criada antes do rastreamento completo de dependências."));
        }
        if (string.IsNullOrWhiteSpace(currentPortfolioSnapshotKey) || assessment.PortfolioSnapshotKey != currentPortfolioSnapshotKey)
            reasons.Add(new(FreshnessDependencyType.PortfolioSnapshot, assessment.PortfolioSnapshotKey, currentPortfolioSnapshotKey, "O estado atual da carteira é diferente do usado nesta avaliação."));
        if (assessment.StrategyVersion != currentStrategyVersion)
            reasons.Add(new(FreshnessDependencyType.Strategy, assessment.StrategyVersion?.ToString(), currentStrategyVersion?.ToString(), "A estratégia usada nesta avaliação não é a estratégia atual."));
        if (assessment.ObjectiveVersion != currentObjectiveVersion)
            reasons.Add(new(FreshnessDependencyType.Objective, assessment.ObjectiveVersion?.ToString(), currentObjectiveVersion?.ToString(), "O objetivo usado nesta avaliação não é o objetivo atual."));
        if (currentEvent is null || assessment.InvestmentEventId != currentEvent.Id || assessment.InvestmentEventVersion != currentEvent.Version)
            reasons.Add(new(FreshnessDependencyType.InvestmentEvent, $"{assessment.InvestmentEventId}:v{assessment.InvestmentEventVersion}", currentEvent is null ? null : $"{currentEvent.Id}:v{currentEvent.Version}", "O evento ou sua versão não corresponde ao contexto atual."));
        if (!string.Equals(assessment.AssessmentVersion, currentAssessmentPolicyVersion, StringComparison.Ordinal))
            reasons.Add(new(FreshnessDependencyType.AssessmentPolicy, assessment.AssessmentVersion, currentAssessmentPolicyVersion, "A policy de assessment contextual é diferente da policy atual."));

        if (reasons.Any(x => x.DependencyType == FreshnessDependencyType.MissingMetadata)) return new(FreshnessStatus.Unknown, reasons);
        return reasons.Count == 0 ? FreshnessResult.Fresh() : new(FreshnessStatus.Stale, reasons);
    }
}
