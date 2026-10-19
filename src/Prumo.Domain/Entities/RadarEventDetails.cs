namespace Prumo.Domain.Entities;

// Optional for old analyses; all source fields are literal document excerpts.
public sealed record RadarEventDetails(
    string? OperationId = null, string? Stage = null, string? Deadline = null,
    string? Amount = null, string? OutstandingDebt = null, string? Restriction = null,
    string? Audience = null, string? ContractTerms = null, string? Evidence = null,
    string? Importance = null, string? ImpactDirection = null, string? Applicability = null,
    string? AttentionBasis = null, string? AttentionReason = null, string? AttentionEvidence = null,
    string? ConsequenceEvidence = null, string? UrgencyEvidence = null,
    string? NoveltyCandidate = null, string Novelty = "Unknown", string? ContinuityKey = null,
    Guid? PreviousEventId = null, string Materiality = "Unknown");
