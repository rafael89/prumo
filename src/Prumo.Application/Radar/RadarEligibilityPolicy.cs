using Prumo.Application.Documents;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Application.Radar;

public static class RadarEligibilityPolicy
{
    public static bool IsEligible(InvestmentEvent investmentEvent)
    {
        var analysis = investmentEvent.DocumentAnalysis;
        var document = analysis?.Document;
        return IsEligible(investmentEvent.EventType, analysis?.Classification,
            document?.SourceCategory, document?.SourceType, document?.SourceSpecies, investmentEvent.RadarDetails);
    }

    // Apply at read time too: old AI classifications must not keep offering
    // paperwork in the Radar or turn it into a portfolio restriction.
    public static bool IsEligible(InvestmentEventType eventType, AnalysisClassification? classification,
        string? sourceCategory, string? sourceType, string? sourceSpecies, RadarEventDetails? details = null)
    {
        if (eventType == InvestmentEventType.CapitalRaise
            || (classification == AnalysisClassification.CapitalRaise
                && eventType is InvestmentEventType.Other or InvestmentEventType.Operational)
            || DocumentClassificationPolicy.IsOperationalIssuanceDocument(sourceCategory, sourceType, sourceSpecies))
            return false;

        if (details?.AttentionBasis is "DEADLINE" or "DECISION" && !string.IsNullOrWhiteSpace(details.AttentionEvidence))
            return true;
        if (details?.ImpactDirection == "Positive")
            return false;

        // A report can put a favorable transaction and its ordinary obligations in the same event.
        // Require an adverse direction or a documented consequence to raise that report as an alert.
        return eventType is not (InvestmentEventType.AssetTransaction or InvestmentEventType.Operational)
            || !DocumentClassificationPolicy.IsPeriodicReport(sourceCategory, sourceType)
            || details?.ImpactDirection == "Negative"
            || !string.IsNullOrWhiteSpace(details?.ConsequenceEvidence);
    }
}
