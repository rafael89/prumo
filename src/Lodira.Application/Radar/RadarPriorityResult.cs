using Lodira.Domain.Enums;

namespace Lodira.Application.Radar;

public sealed record RadarPriorityResult(RadarPriority Priority, int Score, IReadOnlyList<RadarReason> Reasons);
