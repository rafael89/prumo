using Lodira.Domain.Enums;

namespace Lodira.Application.Radar;

/// <summary>Single definition of the derived Radar attention state.</summary>
public static class RadarAttentionPolicy
{
    public static bool RequiresAttention(RadarPriority priority, bool requiresAction, RadarItemStatus status)
        => status != RadarItemStatus.Resolved && (priority >= RadarPriority.High || requiresAction);
}
