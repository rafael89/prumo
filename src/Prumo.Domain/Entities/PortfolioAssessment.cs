using Prumo.Domain.Enums;
namespace Prumo.Domain.Entities;
public sealed class PortfolioAssessment
{
    private PortfolioAssessment() { }
    public PortfolioAssessment(Guid portfolioId, Guid investmentEventId, AttentionPriority attentionPriority, ActionRequirement actionRequirement, CapitalDecisionImpact capitalDecisionImpact, AssessmentStatus status, AssessmentUserState userState, string summary, string whyItMatters, string suggestedBehavior, IntelligenceConfidence confidence, string? targetAllocationKey, string assessmentVersion = "portfolio-impact-v1", string? portfolioSnapshotKey = null, int? investmentEventVersion = null)
    {
        if (portfolioId == Guid.Empty || investmentEventId == Guid.Empty) throw new ArgumentException("Carteira e evento são obrigatórios.");
        if (string.IsNullOrWhiteSpace(summary) || string.IsNullOrWhiteSpace(whyItMatters) || string.IsNullOrWhiteSpace(suggestedBehavior)) throw new ArgumentException("Os textos do assessment são obrigatórios.");
        if (string.IsNullOrWhiteSpace(assessmentVersion)) throw new ArgumentException("A versão do assessment é obrigatória.", nameof(assessmentVersion));
        Id = Guid.NewGuid(); PortfolioId = portfolioId; InvestmentEventId = investmentEventId; AttentionPriority = attentionPriority; ActionRequirement = actionRequirement; CapitalDecisionImpact = capitalDecisionImpact; Status = status; UserState = userState; Summary = summary.Trim(); WhyItMatters = whyItMatters.Trim(); SuggestedBehavior = suggestedBehavior.Trim(); Confidence = confidence; TargetAllocationKey = string.IsNullOrWhiteSpace(targetAllocationKey) ? null : targetAllocationKey.Trim(); AssessmentVersion = assessmentVersion.Trim(); PortfolioSnapshotKey = string.IsNullOrWhiteSpace(portfolioSnapshotKey) ? null : portfolioSnapshotKey.Trim(); InvestmentEventVersion = investmentEventVersion; GeneratedAt = DateTimeOffset.UtcNow;
    }
    public Guid Id { get; private set; } public Guid PortfolioId { get; private set; } public Guid InvestmentEventId { get; private set; }
    public AttentionPriority AttentionPriority { get; private set; } public ActionRequirement ActionRequirement { get; private set; } public CapitalDecisionImpact CapitalDecisionImpact { get; private set; }
    public AssessmentStatus Status { get; private set; } public AssessmentUserState UserState { get; private set; } public string Summary { get; private set; } = string.Empty; public string WhyItMatters { get; private set; } = string.Empty; public string SuggestedBehavior { get; private set; } = string.Empty; public IntelligenceConfidence Confidence { get; private set; }
    public string? TargetAllocationKey { get; private set; } public string AssessmentVersion { get; private set; } = string.Empty; public string? PortfolioSnapshotKey { get; private set; } public int? InvestmentEventVersion { get; private set; } public DateTimeOffset GeneratedAt { get; private set; }
    public InvestmentEvent? InvestmentEvent { get; private set; } public void AttachInvestmentEvent(InvestmentEvent investmentEvent) => InvestmentEvent = investmentEvent; public void MarkSeen() => UserState = AssessmentUserState.Seen; public void SetStatus(AssessmentStatus status) => Status = status;
}
