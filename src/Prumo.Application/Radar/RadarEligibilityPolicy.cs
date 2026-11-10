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
            document?.SourceCategory, document?.SourceType, document?.SourceSpecies);
    }

    // Apply at read time too: old AI classifications must not keep offering
    // paperwork in the Radar or turn it into a portfolio restriction.
    public static bool IsEligible(InvestmentEventType eventType, AnalysisClassification? classification,
        string? sourceCategory, string? sourceType, string? sourceSpecies)
        => eventType != InvestmentEventType.CapitalRaise
            && !(classification == AnalysisClassification.CapitalRaise
                && eventType is InvestmentEventType.Other or InvestmentEventType.Operational)
            && !DocumentClassificationPolicy.IsOperationalIssuanceDocument(sourceCategory, sourceType, sourceSpecies);
}
