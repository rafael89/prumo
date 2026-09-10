using Prumo.Application.Explainability;
using Prumo.Application.Abstractions;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Application.Contributions;

public enum CandidateIntelligenceSectionStatus { Available, Partial, Unavailable, Stale }
public enum CandidateIntelligenceCoverage { High, Medium, Low }

public sealed record CandidateIntelligenceSection(
    CandidateIntelligenceSectionStatus Status,
    DateTimeOffset? ObservedAt,
    IReadOnlyList<EvidenceReference> Evidence,
    decimal? Confidence,
    IReadOnlyList<string> MissingData);

public sealed record CandidateIntelligenceEvent(
    InvestmentEventType Type,
    DateTimeOffset? OccurredAt,
    string Summary,
    IReadOnlyList<string> Facts,
    IReadOnlyList<EvidenceReference> Evidence);

public sealed record CandidateIntelligence(
    CandidateKey CandidateKey,
    CandidateIntelligenceSection Structural,
    CandidateIntelligenceSection Fundamentals,
    CandidateIntelligenceSection Valuation,
    CandidateIntelligenceSection IncomeQuality,
    IReadOnlyList<CandidateIntelligenceEvent> RecentEvents,
    IReadOnlyList<ContributionAssessmentContext> Assessments,
    IReadOnlyList<EvidenceReference> Evidence,
    CandidateIntelligenceCoverage Coverage)
{
    public static CandidateIntelligence Unavailable(ContributionCandidate candidate) => new(
        candidate.CandidateKey,
        new(CandidateIntelligenceSectionStatus.Unavailable, null, Array.Empty<EvidenceReference>(), null, new[] { "Metadados estruturais indisponíveis." }),
        new(CandidateIntelligenceSectionStatus.Unavailable, null, Array.Empty<EvidenceReference>(), null, new[] { "Fundamentos indisponíveis." }),
        new(CandidateIntelligenceSectionStatus.Unavailable, null, Array.Empty<EvidenceReference>(), null, new[] { "Valuation indisponível." }),
        new(CandidateIntelligenceSectionStatus.Unavailable, null, Array.Empty<EvidenceReference>(), null, new[] { "Dados de renda/distribuição indisponíveis." }),
        Array.Empty<CandidateIntelligenceEvent>(), candidate.CurrentAssessments, Array.Empty<EvidenceReference>(), CandidateIntelligenceCoverage.Low);
}

public static class CandidateIntelligenceBuilder
{
    public static CandidateIntelligence Build(
        ContributionCandidate candidate,
        Asset asset,
        IReadOnlyList<InvestmentEvent> events,
        IReadOnlyList<ContributionAssessmentContext> assessments)
    {
        var structural = new CandidateIntelligenceSection(
            string.IsNullOrWhiteSpace(asset.Ticker) || asset.AssetClass == AssetClass.Other
                ? CandidateIntelligenceSectionStatus.Partial : CandidateIntelligenceSectionStatus.Available,
            null, Array.Empty<EvidenceReference>(), null,
            asset.AssetClass == AssetClass.Other ? new[] { "Classe do ativo indisponível." } : Array.Empty<string>());

        var financial = CandidateFinancialEvaluator.Evaluate(asset.Id, asset.AssetType, candidate.FinancialSnapshot);
        var fundamentals = FinancialSection(financial.Quality.Coverage, financial.Quality.Freshness, candidate.FinancialSnapshot?.ObservedAt,
            candidate.FinancialSnapshot?.SourceReference, "Fundamentos", financial.Quality.Evidence.Count, financial.Quality.MissingData);
        var valuation = FinancialSection(financial.Valuation.Coverage, financial.Valuation.Freshness, candidate.FinancialSnapshot?.ObservedAt,
            candidate.FinancialSnapshot?.SourceReference, "Valuation", financial.Valuation.Metrics.Count, financial.Valuation.MissingData);

        var incomeAvailable = candidate.FinancialSnapshot?.DividendYield is not null;
        var income = new CandidateIntelligenceSection(
            incomeAvailable ? Freshness(candidate.FinancialSnapshot!.ObservedAt, TimeSpan.FromDays(35)) : CandidateIntelligenceSectionStatus.Unavailable,
            incomeAvailable ? candidate.FinancialSnapshot!.ObservedAt : null,
            Reference(candidate.FinancialSnapshot?.SourceReference, "FinancialSnapshot", candidate.FinancialSnapshot?.ObservedAt),
            incomeAvailable ? 1m : null,
            incomeAvailable ? Array.Empty<string>() : new[] { "Dados de renda/distribuição indisponíveis." });

        var eventContexts = events
            .Where(x => x.AffectedAssets.Any(a => a.Id == asset.Id))
            .OrderByDescending(x => x.OccurredAt ?? x.CreatedAt)
            .Select(x => new CandidateIntelligenceEvent(x.EventType, x.OccurredAt, x.Summary, x.Facts,
                x.Evidence.Select(e => new EvidenceReference("Document", e.DocumentId.ToString(), e.Reference, e.Reference, x.OccurredAt)).ToArray()))
            .ToArray();
        var eventEvidence = eventContexts.SelectMany(x => x.Evidence).Distinct().ToArray();
        var allEvidence = fundamentals.Evidence.Concat(valuation.Evidence).Concat(income.Evidence).Concat(eventEvidence)
            .Concat(assessments.SelectMany(x => x.EvidenceReferences)).Distinct().ToArray();
        // Structural metadata identifies the candidate but does not increase
        // the quality/completeness of financial or event intelligence.
        var intelligenceSections = new[] { fundamentals.Status, valuation.Status, income.Status };
        var availableCount = intelligenceSections.Count(x => x is CandidateIntelligenceSectionStatus.Available or CandidateIntelligenceSectionStatus.Partial) + (eventContexts.Length > 0 ? 1 : 0) + (assessments.Count > 0 ? 1 : 0);
        var unavailableCount = intelligenceSections.Count(x => x == CandidateIntelligenceSectionStatus.Unavailable) + (eventContexts.Length == 0 ? 1 : 0) + (assessments.Count == 0 ? 1 : 0);
        var coverage = unavailableCount == 0 && !intelligenceSections.Contains(CandidateIntelligenceSectionStatus.Stale)
            ? CandidateIntelligenceCoverage.High : availableCount > 0 ? CandidateIntelligenceCoverage.Medium : CandidateIntelligenceCoverage.Low;
        return new(candidate.CandidateKey, structural, fundamentals, valuation, income, eventContexts, assessments, allEvidence, coverage);
    }

    private static CandidateIntelligenceSection FinancialSection(FinancialEvidenceCoverage coverage, FinancialEvidenceFreshness freshness, DateTimeOffset? observedAt, string? source, string label, int evidenceCount, IReadOnlyList<string> missing)
    {
        var status = coverage == FinancialEvidenceCoverage.Unavailable ? CandidateIntelligenceSectionStatus.Unavailable
            : freshness == FinancialEvidenceFreshness.Stale ? CandidateIntelligenceSectionStatus.Stale
            : evidenceCount == 0 ? CandidateIntelligenceSectionStatus.Partial : coverage == FinancialEvidenceCoverage.Complete ? CandidateIntelligenceSectionStatus.Available : CandidateIntelligenceSectionStatus.Partial;
        return new(status, observedAt, Reference(source, "FinancialSnapshot", observedAt), evidenceCount > 0 ? 1m : null, missing);
    }

    private static CandidateIntelligenceSectionStatus Freshness(DateTimeOffset observedAt, TimeSpan maxAge)
        => DateTimeOffset.UtcNow - observedAt <= maxAge ? CandidateIntelligenceSectionStatus.Available : CandidateIntelligenceSectionStatus.Stale;

    private static IReadOnlyList<EvidenceReference> Reference(string? source, string type, DateTimeOffset? observedAt)
        => string.IsNullOrWhiteSpace(source) ? Array.Empty<EvidenceReference>() : new[] { new EvidenceReference(type, source, source, null, observedAt) };
}
