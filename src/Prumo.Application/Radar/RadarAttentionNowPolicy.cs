using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Application.Radar;

public static class RadarAttentionNowPolicy
{
    public const string Version = "radar-attention-v3";

    public static bool RequiresAttention(PortfolioAssessment assessment)
        => RequiresAttention(assessment.Status, assessment.AttentionPriority, assessment.ActionRequirement, assessment.CapitalDecisionImpact);

    public static bool RequiresAttention(AssessmentStatus status, AttentionPriority priority, ActionRequirement action, CapitalDecisionImpact impact)
        => status != AssessmentStatus.Closed
            && (action is ActionRequirement.Review or ActionRequirement.Act
                || impact is CapitalDecisionImpact.Caution or CapitalDecisionImpact.Block);
}
