using Prumo.Application.Radar;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class RadarAttentionPolicyTests
{
    [Theory]
    [InlineData(RadarPriority.High, false, RadarItemStatus.Unread, true)]
    [InlineData(RadarPriority.Medium, true, RadarItemStatus.Unread, true)]
    [InlineData(RadarPriority.Medium, false, RadarItemStatus.Unread, false)]
    [InlineData(RadarPriority.High, true, RadarItemStatus.Resolved, false)]
    public void Requires_attention_uses_the_shared_semantics(RadarPriority priority, bool requiresAction, RadarItemStatus status, bool expected)
        => Assert.Equal(expected, RadarAttentionPolicy.RequiresAttention(priority, requiresAction, status));

    [Fact]
    public void Multiple_events_are_review_required_when_any_event_requires_attention()
    {
        var events = new[]
        {
            RadarAttentionPolicy.RequiresAttention(RadarPriority.Medium, false, RadarItemStatus.Resolved),
            RadarAttentionPolicy.RequiresAttention(RadarPriority.Medium, true, RadarItemStatus.Unread)
        };

        Assert.Contains(true, events);
    }

    [Fact]
    public void Multiple_events_are_eligible_when_all_relevant_events_are_resolved()
    {
        var events = new[]
        {
            RadarAttentionPolicy.RequiresAttention(RadarPriority.High, false, RadarItemStatus.Resolved),
            RadarAttentionPolicy.RequiresAttention(RadarPriority.Medium, true, RadarItemStatus.Resolved)
        };

        Assert.DoesNotContain(true, events);
    }
}
