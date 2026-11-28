using Prumo.Domain.Enums;

namespace Prumo.Application.Contributions;

public static class ContributionEventContext
{
    public static DateTimeOffset Cutoff(DateTimeOffset asOf) => asOf.AddDays(-120);

    public static IReadOnlyList<CandidateIntelligenceEvent> SelectForPrompt(CandidateIntelligence intelligence, DateTimeOffset asOf)
    {
        var cutoff = Cutoff(asOf);
        var decisionEvents = intelligence.Assessments.Where(HasDecisionImpact)
            .Select(x => x.InvestmentEventId).ToHashSet();

        return intelligence.RecentEvents
            .Select(evt => new { Event = evt, Date = evt.PublishedAt ?? evt.OccurredAt, IsDecisionEvent = decisionEvents.Contains(evt.EventId) })
            .Where(x => x.IsDecisionEvent || (x.Date >= cutoff && x.Date <= asOf))
            .OrderByDescending(x => x.IsDecisionEvent)
            .ThenByDescending(x => EventPriority(x.Event.Type))
            .ThenByDescending(x => x.Date)
            .ThenBy(x => x.Event.EventId)
            .Take(5)
            .Select(x => x.Event)
            .ToArray();
    }

    private static bool HasDecisionImpact(ContributionAssessmentContext assessment)
        => assessment.CapitalDecisionImpact is nameof(CapitalDecisionImpact.Block) or nameof(CapitalDecisionImpact.Caution) or nameof(CapitalDecisionImpact.Favor)
            || assessment.ActionRequirement is nameof(ActionRequirement.Review) or nameof(ActionRequirement.Act);

    private static int EventPriority(InvestmentEventType type) => type switch
    {
        InvestmentEventType.Liquidation => 2,
        InvestmentEventType.FinancialResult or InvestmentEventType.CapitalRaise or InvestmentEventType.CorporateAction or InvestmentEventType.AssetTransaction => 1,
        InvestmentEventType.Distribution => -1,
        _ => 0
    };
}
