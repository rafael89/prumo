using Prumo.Application.Intelligence;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Application.Radar;

public static class RadarAssessmentSelector
{
    public static PortfolioAssessment SelectRepresentative(IEnumerable<PortfolioAssessment> assessments)
        => assessments
            .OrderByDescending(x => x.Status != AssessmentStatus.Closed)
            .ThenByDescending(RadarAttentionNowPolicy.RequiresAttention)
            .ThenByDescending(x => x.UserState == AssessmentUserState.Unread)
            .ThenByDescending(x => x.AttentionPriority)
            .ThenByDescending(x => x.AssessmentVersion == PortfolioImpactAssessmentService.CurrentVersion)
            .ThenByDescending(x => x.GeneratedAt)
            .ThenByDescending(x => x.Id)
            .First();
}
