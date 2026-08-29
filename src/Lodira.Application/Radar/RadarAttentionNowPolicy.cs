using Lodira.Domain.Entities;
using Lodira.Domain.Enums;

namespace Lodira.Application.Radar;

public static class RadarAttentionNowPolicy
{
    public const string Version = "radar-attention-v2";

    public static bool RequiresAttention(PortfolioAssessment assessment)
        => RequiresAttention(assessment.Status, assessment.AttentionPriority, assessment.ActionRequirement, assessment.CapitalDecisionImpact);

    public static bool RequiresAttention(AssessmentStatus status, AttentionPriority priority, ActionRequirement action, CapitalDecisionImpact impact)
        => status != AssessmentStatus.Closed
            && (priority >= AttentionPriority.High || action is ActionRequirement.Review or ActionRequirement.Act || impact is CapitalDecisionImpact.Caution or CapitalDecisionImpact.Block);
}
