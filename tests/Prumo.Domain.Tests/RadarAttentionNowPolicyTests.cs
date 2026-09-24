using Prumo.Application.Radar;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class RadarAttentionNowPolicyTests
{
    [Theory]
    [InlineData(AttentionPriority.High, ActionRequirement.None, CapitalDecisionImpact.None, false)]
    [InlineData(AttentionPriority.Critical, ActionRequirement.None, CapitalDecisionImpact.None, false)]
    [InlineData(AttentionPriority.High, ActionRequirement.Monitor, CapitalDecisionImpact.None, false)]
    [InlineData(AttentionPriority.Low, ActionRequirement.Monitor, CapitalDecisionImpact.None, false)]
    [InlineData(AttentionPriority.Low, ActionRequirement.Review, CapitalDecisionImpact.None, true)]
    [InlineData(AttentionPriority.High, ActionRequirement.None, CapitalDecisionImpact.Caution, true)]
    public void Exposure_priority_alone_does_not_require_attention(AttentionPriority priority, ActionRequirement action, CapitalDecisionImpact impact, bool expected)
        => Assert.Equal(expected, RadarAttentionNowPolicy.RequiresAttention(AssessmentStatus.Detected, priority, action, impact));

    [Fact]
    public void Closed_assessment_does_not_require_attention()
        => Assert.False(RadarAttentionNowPolicy.RequiresAttention(AssessmentStatus.Closed, AttentionPriority.High, ActionRequirement.Review, CapitalDecisionImpact.Caution));
}
