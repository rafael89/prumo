namespace Prumo.Application.Explainability;

/// <summary>Stable pointer to an existing product fact; it is not a copy of that fact.</summary>
public sealed record EvidenceReference(string Type, string ReferenceId, string Label, string? Summary = null, DateTimeOffset? OccurredAt = null);
