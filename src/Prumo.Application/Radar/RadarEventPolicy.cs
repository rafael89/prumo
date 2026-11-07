using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Application.Radar;

public static class RadarEventPolicy
{
    public static string Normalize(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static bool IsLiteral(string source, string? evidence)
        => !string.IsNullOrWhiteSpace(evidence) && Normalize(source).Contains(Normalize(evidence), StringComparison.OrdinalIgnoreCase);

    public static RadarEventDetails Validate(RadarEventDetails? input, string source, AnalysisImportance fallback)
    {
        var d = input ?? new();
        string? Literal(string? value) => IsLiteral(source, value) ? Normalize(value!) : null;
        var evidence = Literal(d.Evidence);
        string? Field(string? value) => evidence is not null && IsLiteral(evidence, value) ? Normalize(value!) : null;
        var operationId = Field(d.OperationId);
        if (operationId is not null && (!operationId.Any(char.IsDigit)
            || !Regex.IsMatch(operationId, @"\b(contrato|emiss[aã]o|processo|opera[cç][aã]o|documento|protocolo|instrumento)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            operationId = null;
        var importance = Enum.TryParse<AnalysisImportance>(d.Importance?.Replace("_", ""), true, out var parsed) && Enum.IsDefined(parsed) ? parsed : fallback;
        var consequence = Literal(d.ConsequenceEvidence);
        var urgency = Literal(d.UrgencyEvidence);
        if (importance == AnalysisImportance.Critical && (consequence is null || urgency is null)) importance = AnalysisImportance.Relevant;
        var basis = d.AttentionBasis?.ToUpperInvariant();
        var attentionEvidence = Literal(d.AttentionEvidence);
        if (basis is not ("CHANGE" or "DEADLINE" or "DECISION") || attentionEvidence is null || string.IsNullOrWhiteSpace(d.AttentionReason)) basis = "UNKNOWN";
        return new(operationId, Field(d.Stage), Field(d.Deadline), Field(d.Amount), Field(d.OutstandingDebt),
            Field(d.Restriction), Field(d.Audience), Field(d.ContractTerms), evidence, importance.ToString(),
            evidence is not null && (d.ImpactDirection is "Positive" or "Negative" or "Mixed" or "Neutral") ? d.ImpactDirection : "Indeterminate",
            Literal(d.Applicability), basis, d.AttentionReason, attentionEvidence, consequence, urgency);
    }

    public static RadarEventDetails Classify(RadarEventDetails details, InvestmentEventType type, IEnumerable<Guid> assets, IReadOnlyList<InvestmentEvent> recent)
    {
        var ids = assets.Order().ToArray();
        // shortcut: identity requires an explicit operation identifier; add other anchors only with equivalent proof.
        if (details.OperationId is null || ids.Length == 0 || type == InvestmentEventType.Distribution) return details;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{string.Join(',', ids)}|{type}|{details.OperationId.ToUpperInvariant()}")));
        var previous = recent.Where(e => e.RadarDetails?.ContinuityKey == key)
            .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).FirstOrDefault();
        if (previous is null) return details with { Novelty = "New", ContinuityKey = key };
        var before = State(previous.RadarDetails!);
        var after = State(details);
        var changed = before.Zip(after).Any(pair => pair.First is not null && pair.Second is not null && !string.Equals(pair.First, pair.Second, StringComparison.OrdinalIgnoreCase));
        var complete = before.Zip(after).All(pair => (pair.First is null) == (pair.Second is null));
        var evidencedChange = (details.AttentionBasis is "CHANGE" or "DEADLINE" or "DECISION") && details.AttentionEvidence is not null;
        var sameEvidence = string.Equals(previous.RadarDetails!.Evidence, details.Evidence, StringComparison.OrdinalIgnoreCase);
        var novelty = changed && evidencedChange ? "Update" : !changed && complete && sameEvidence && after.Any(x => x is not null) ? "Repeat" : "Unknown";
        return details with { Novelty = novelty, ContinuityKey = novelty == "Unknown" ? null : key, PreviousEventId = previous.Id,
            Importance = novelty == "Repeat" ? previous.RadarDetails!.Importance : details.Importance };
    }

    private static string?[] State(RadarEventDetails d) => [d.Stage, d.Deadline, d.Amount, d.OutstandingDebt, d.Restriction, d.Audience, d.ContractTerms];

    public static AnalysisImportance Importance(InvestmentEvent e, AnalysisImportance? fallback = null)
        => Enum.TryParse<AnalysisImportance>(e.RadarDetails?.Importance, out var importance) ? importance : fallback ?? e.DocumentAnalysis?.Importance ?? AnalysisImportance.Informative;

    // Structured event details describe individual operations inside the document;
    // they do not turn those operations into separate Radar cards.
    public static Guid CardId(InvestmentEvent e) => e.DocumentAnalysisId;

    public static IReadOnlyList<PortfolioAssessment> Latest(IEnumerable<PortfolioAssessment> assessments)
        => assessments.GroupBy(a => a.InvestmentEvent?.RadarDetails?.ContinuityKey ?? a.InvestmentEventId.ToString())
            .SelectMany(group =>
            {
                var latestId = group.OrderByDescending(a => a.InvestmentEvent?.CreatedAt).ThenByDescending(a => a.InvestmentEventId).First().InvestmentEventId;
                return group.Where(a => a.InvestmentEventId == latestId);
            }).ToList();
}
