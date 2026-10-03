using Prumo.Application.Abstractions;
using Prumo.Application.Documents;
using Prumo.Application.Intelligence;
using Prumo.Application.Radar;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class RadarMaterialChangeTests
{
    [Theory]
    [InlineData(null, null, null, 4.19)]
    [InlineData("UNKNOWN", "Litígio conhecido; novidade não determinada", "", 10)]
    [InlineData("NONE", "Atualização do processo", "", 6)]
    [InlineData("CHANGE", "Agravamento", "", 6)]
    public void Risk_without_documented_change_remains_monitoring(string? basis, string? reason, string? evidence, decimal weight)
    {
        var result = Assess(basis, reason, evidence, weight);
        Assert.Equal(ActionRequirement.Monitor, result.ActionRequirement);
        Assert.Equal(CapitalDecisionImpact.None, result.CapitalDecisionImpact);
        Assert.False(RadarAttentionNowPolicy.RequiresAttention(AssessmentStatus.Detected, result.AttentionPriority, result.ActionRequirement, result.CapitalDecisionImpact));
        Assert.Contains("novidade do risco não foi estabelecida", result.WhyItMatters);
        Assert.DoesNotContain("Impacto potencial perceptível", result.WhyItMatters);
    }

    [Theory]
    [InlineData("CHANGE")]
    [InlineData("DEADLINE")]
    [InlineData("DECISION")]
    public void Documented_trigger_can_require_review_and_explains_why(string basis)
    {
        var result = Assess(basis, "A inadimplência aumentou", "Aumento de 1% para 8%", 4.19m);
        Assert.Equal(ActionRequirement.Review, result.ActionRequirement);
        Assert.Contains("A inadimplência aumentou", result.WhyItMatters);
        Assert.Contains("Aumento de 1% para 8%", result.WhyItMatters);
        Assert.True(RadarAttentionNowPolicy.RequiresAttention(AssessmentStatus.Detected, result.AttentionPriority, result.ActionRequirement, result.CapitalDecisionImpact));
    }

    [Fact]
    public void Validator_removes_action_when_novelty_is_unknown()
    {
        var payload = new AiAnalysisPayload("Relevant", "FinancialResult", true, "EvaluateSale", "Balanço", "Litígio conhecido", null, ["GGRC11"], 0.9m,
            AttentionBasis: "UNKNOWN", AttentionReason: "Não há comparação", AttentionEvidence: "");
        var result = new AiAnalysisValidator().Validate(payload, "{}");
        Assert.False(result.RequiresAction);
        Assert.Equal(SuggestedAction.Monitor, result.SuggestedAction);
    }

    private static PortfolioImpactAssessmentOutput Assess(string? basis, string? reason, string? evidence, decimal weight)
    {
        var portfolio = new Portfolio("Teste");
        var analysis = new DocumentAnalysis(Guid.NewGuid(), Guid.NewGuid(), "test", "v8", AnalysisImportance.Relevant,
            AnalysisClassification.FinancialResult, true, SuggestedAction.Monitor, "Balanço", "Litígio", "Impacto", ["GGRC11"], 0.9m, "{}", basis, reason, evidence);
        var investmentEvent = new InvestmentEvent(analysis.Id, InvestmentEventType.FinancialResult, null, "Litígio com locatária", []);
        typeof(InvestmentEvent).GetProperty(nameof(InvestmentEvent.DocumentAnalysis))!.SetValue(investmentEvent, analysis);
        return new PortfolioImpactAssessmentService().AssessPortfolioImpact(new(investmentEvent, portfolio,
            [new RelevantPositionContext(Guid.NewGuid(), "GGRC11", AssetClass.Fii, 100, weight)], null,
            AnalysisImportance.Relevant, true, SuggestedAction.Monitor));
    }
}
