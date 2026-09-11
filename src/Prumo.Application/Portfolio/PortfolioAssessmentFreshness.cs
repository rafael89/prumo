using Prumo.Application.Abstractions;
using Prumo.Domain.Entities;
namespace Prumo.Application.Portfolio;
public enum FreshnessStatus { Fresh, Stale, Unknown }
public enum FreshnessDependencyType { PortfolioSnapshot, TargetAllocation, InvestmentEvent, AssessmentPolicy, MissingMetadata }
public sealed record FreshnessReason(FreshnessDependencyType DependencyType, string? ExpectedVersionOrKey, string? CurrentVersionOrKey, string Reason);
public sealed record FreshnessResult(FreshnessStatus Status, IReadOnlyList<FreshnessReason> Reasons) { public static FreshnessResult Fresh() => new(FreshnessStatus.Fresh, Array.Empty<FreshnessReason>()); }
public sealed class PortfolioAssessmentFreshnessEvaluator : IPortfolioAssessmentFreshnessEvaluator
{
    public FreshnessResult Evaluate(PortfolioAssessment assessment, string? snapshot, string? targetKey, InvestmentEvent? currentEvent, string policyVersion)
    {
        var reasons = new List<FreshnessReason>();
        if (string.IsNullOrWhiteSpace(assessment.PortfolioSnapshotKey) || !assessment.InvestmentEventVersion.HasValue) reasons.Add(new(FreshnessDependencyType.MissingMetadata, null, null, "A avaliação não possui metadados completos."));
        if (assessment.PortfolioSnapshotKey != snapshot) reasons.Add(new(FreshnessDependencyType.PortfolioSnapshot, assessment.PortfolioSnapshotKey, snapshot, "O estado atual da carteira é diferente do usado nesta avaliação."));
        if (assessment.TargetAllocationKey != targetKey) reasons.Add(new(FreshnessDependencyType.TargetAllocation, assessment.TargetAllocationKey, targetKey, "A alocação-alvo atual é diferente da usada nesta avaliação."));
        if (currentEvent is null || assessment.InvestmentEventId != currentEvent.Id || assessment.InvestmentEventVersion != currentEvent.Version) reasons.Add(new(FreshnessDependencyType.InvestmentEvent, $"{assessment.InvestmentEventId}:v{assessment.InvestmentEventVersion}", currentEvent is null ? null : $"{currentEvent.Id}:v{currentEvent.Version}", "O evento ou sua versão não corresponde ao contexto atual."));
        if (!string.Equals(assessment.AssessmentVersion, policyVersion, StringComparison.Ordinal)) reasons.Add(new(FreshnessDependencyType.AssessmentPolicy, assessment.AssessmentVersion, policyVersion, "A policy de assessment é diferente da atual."));
        if (reasons.Any(x => x.DependencyType == FreshnessDependencyType.MissingMetadata)) return new(FreshnessStatus.Unknown, reasons);
        return reasons.Count == 0 ? FreshnessResult.Fresh() : new(FreshnessStatus.Stale, reasons);
    }
}
