using Prumo.Domain.Enums;

namespace Prumo.Application.Radar;

public sealed record RadarPriorityResult(RadarPriority Priority, int Score, IReadOnlyList<RadarReason> Reasons);
