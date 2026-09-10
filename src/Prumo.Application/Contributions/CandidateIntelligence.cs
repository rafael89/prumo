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
    Guid EventId,
    InvestmentEventType Type,
    DateTimeOffset? OccurredAt,
    string Summary,
    IReadOnlyList<string> Facts,
    IReadOnlyList<EvidenceReference> Evidence,
    bool SummaryTruncated = false,
    int OriginalSummaryLength = 0);

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
    public IReadOnlyList<MetricEvidenceResolution> MetricEvidence { get; init; } = Array.Empty<MetricEvidenceResolution>();
    public IReadOnlyList<CandidateDimensionCoverage> DimensionalCoverage { get; init; } = Array.Empty<CandidateDimensionCoverage>();
    public CandidateFreshness? Freshness { get; init; }

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
    public static CandidateIntelligence ProjectForR5(CandidateIntelligence intelligence)
    {
        var eventReferences = intelligence.RecentEvents.SelectMany(x => x.Evidence).ToHashSet();
        var assessmentReferences = intelligence.Assessments.SelectMany(x => x.EvidenceReferences).ToHashSet();
        var sectionReferences = intelligence.Structural.Evidence.Concat(intelligence.Fundamentals.Evidence)
            .Concat(intelligence.Valuation.Evidence).Concat(intelligence.IncomeQuality.Evidence).ToHashSet();
        var indexedOnly = intelligence.Evidence
            .Where(reference => !eventReferences.Contains(reference) && !assessmentReferences.Contains(reference) && !sectionReferences.Contains(reference))
            .Distinct().ToArray();
        return intelligence with { Evidence = indexedOnly };
    }

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
        var metricEvidence = MetricEvidenceResolver.Resolve(asset);
        var fundType = candidate.FundType == FundType.Unknown ? SectorExposureSemantics.InferFundType(asset.AssetType, asset.Sector) : candidate.FundType;
        var dimensions = Enum.GetValues<CandidateIntelligenceDimension>()
            .Where(x => x is not CandidateIntelligenceDimension.Events and not CandidateIntelligenceDimension.Assessments)
            .Select(x => MetricApplicabilityResolver.Dimension(x, asset.AssetType, fundType, metricEvidence, DateTimeOffset.UtcNow)).ToArray();
        dimensions = dimensions.Concat(new[]
        {
            new CandidateDimensionCoverage(CandidateIntelligenceDimension.Events, eventCoverage(events, asset.Id), 0, events.Count(x => x.AffectedAssets.Any(a => a.Id == asset.Id)), 0, Array.Empty<string>(), null),
            new CandidateDimensionCoverage(CandidateIntelligenceDimension.Assessments, assessments.Count > 0 ? MetricApplicability.Available : MetricApplicability.Unavailable, assessments.Count > 0 ? 1 : 0, assessments.Count > 0 ? 1 : 0, 0, assessments.Count > 0 ? Array.Empty<string>() : new[] { "Assessments indisponíveis." }, null)
        }).ToArray();
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
            .Select(x => new CandidateIntelligenceEvent(x.Id, x.EventType, x.OccurredAt, x.Summary, x.Facts,
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
        return new(candidate.CandidateKey, structural, fundamentals, valuation, income, eventContexts, assessments, allEvidence, coverage)
        {
            MetricEvidence = metricEvidence.Metrics,
            DimensionalCoverage = dimensions,
            Freshness = new(CandidateFreshnessPolicy.Version,
                metricEvidence.Metrics.ToDictionary(x => x.Metric, x => x.Canonical is null ? MetricApplicability.Unavailable : CandidateFreshnessPolicy.Evaluate(x.Metric, x.Canonical.ReferenceDate, x.Canonical.ReferencePeriod, x.Canonical.PeriodType, x.Canonical.ObservedAt, DateTimeOffset.UtcNow), StringComparer.OrdinalIgnoreCase),
                dimensions.ToDictionary(x => x.Dimension, x => x.Applicability))
        };
    }

    private static MetricApplicability eventCoverage(IReadOnlyList<InvestmentEvent> events, Guid assetId)
        => events.Any(x => x.AffectedAssets.Any(a => a.Id == assetId)) ? MetricApplicability.Available : MetricApplicability.Unavailable;

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
