using Prumo.Application.Radar;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class RadarPriorityCalculatorTests
{
    private static DocumentAnalysis Analysis(AnalysisImportance importance = AnalysisImportance.NoAction, bool requiresAction = false,
        AnalysisClassification classification = AnalysisClassification.Other, SuggestedAction action = SuggestedAction.NoAction, decimal confidence = 1,
        params string[] assets) => new(Guid.NewGuid(), Guid.NewGuid(), "test", "1", importance, classification, requiresAction, action, "Resumo", "Impacto", "Carteira", assets, confidence, "{}");

    [Fact]
    public void Radar_state_is_independent_per_portfolio()
    {
        var analysisId = Guid.NewGuid();
        var first = new RadarItemState(Guid.NewGuid(), analysisId);
        var second = new RadarItemState(Guid.NewGuid(), analysisId);
        first.SetStatus(RadarItemStatus.Resolved);

        Assert.Equal(RadarItemStatus.Resolved, first.Status);
        Assert.Equal(RadarItemStatus.Unread, second.Status);
        Assert.NotEqual(first.PortfolioId, second.PortfolioId);
    }

    [Fact]
    public void Low_analysis_without_action_is_low() => Assert.Equal(RadarPriority.Low, new RadarPriorityCalculator().Calculate(Analysis()).Priority);

    [Fact]
    public void Relevant_analysis_is_medium_and_action_raises_score() 
    {
        var result = new RadarPriorityCalculator().Calculate(Analysis(AnalysisImportance.Relevant, true));
        Assert.Equal(RadarPriority.High, result.Priority);
        Assert.Contains(RadarReason.RequiresAction, result.Reasons);
    }

    [Fact]
    public void Strong_combination_can_be_critical() => Assert.Equal(RadarPriority.Critical,
        new RadarPriorityCalculator().Calculate(Analysis(AnalysisImportance.Critical, true, AnalysisClassification.Liquidation, SuggestedAction.EvaluateSale, 1, "XPLG11")).Priority);

    [Fact]
    public void Confidence_does_not_change_severity_and_calculation_is_deterministic()
    {
        var calculator = new RadarPriorityCalculator();
        var high = calculator.Calculate(Analysis(AnalysisImportance.Critical, true, confidence: 1));
        var low = calculator.Calculate(Analysis(AnalysisImportance.Critical, true, confidence: .2m));
        Assert.Equal(high.Score, low.Score);
        Assert.Equal(high.Priority, low.Priority);
        Assert.Contains(RadarReason.ReducedConfidence, low.Reasons);
    }

    [Fact]
    public void Radar_state_transitions_keep_timestamps_consistent()
    {
        var state = new RadarItemState(Guid.NewGuid());
        Assert.Equal(RadarItemStatus.Unread, state.Status);
        state.SetStatus(RadarItemStatus.Resolved);
        Assert.NotNull(state.ReadAt);
        Assert.NotNull(state.ResolvedAt);
        state.SetStatus(RadarItemStatus.Read);
        Assert.NotNull(state.ReadAt);
        Assert.Null(state.ResolvedAt);
        state.SetStatus(RadarItemStatus.Unread);
        Assert.Null(state.ReadAt);
        Assert.Null(state.ResolvedAt);
    }
}
