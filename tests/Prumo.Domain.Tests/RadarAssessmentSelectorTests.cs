using Prumo.Application.Radar;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class RadarAssessmentSelectorTests
{
    [Fact]
    public void Selects_event_requiring_attention_over_newer_event_without_action()
    {
        var portfolioId = Guid.NewGuid();
        var action = Assessment(portfolioId, ActionRequirement.Review);
        var update = Assessment(portfolioId, ActionRequirement.None);

        Assert.Same(action, RadarAssessmentSelector.SelectRepresentative([update, action]));
    }

    [Fact]
    public void Open_event_precedes_closed_event_in_same_document()
    {
        var portfolioId = Guid.NewGuid();
        var closed = Assessment(portfolioId, ActionRequirement.Review);
        closed.SetStatus(AssessmentStatus.Closed);
        var open = Assessment(portfolioId, ActionRequirement.None);

        Assert.Same(open, RadarAssessmentSelector.SelectRepresentative([closed, open]));
    }

    private static PortfolioAssessment Assessment(Guid portfolioId, ActionRequirement action)
        => new(portfolioId, Guid.NewGuid(), AttentionPriority.High, action, CapitalDecisionImpact.None,
            AssessmentStatus.Detected, AssessmentUserState.Unread, "Resumo", "Motivo", "Comportamento",
            IntelligenceConfidence.High, null);
}
