using Lodira.Application.Intelligence;
using Lodira.Application.Abstractions;
using Lodira.Application.Radar;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;

namespace Lodira.Domain.Tests;

public sealed class InvestmentEventAssessmentTests
{
    [Fact]
    public void Uma_analise_pode_produzir_zero_eventos()
    {
        var payload = new AiAnalysisPayload("Informative", "Other", false, "NoAction", "Resumo", "Impacto", null, Array.Empty<string>(), .9m);
        var result = new Lodira.Application.Documents.AiAnalysisValidator().Validate(payload, "{}");
        Assert.Empty(result.Events!);
    }

    [Fact]
    public void Evento_preserva_tipo_fatos_ativos_e_versao()
    {
        var analysisId = Guid.NewGuid();
        var investmentEvent = new InvestmentEvent(analysisId, InvestmentEventType.CapitalRaise, DateTimeOffset.UtcNow, "Nova emissão", new[] { "Preço informado", "Cronograma informado" }, 2);
        var asset = new Asset("ABCD3", "Ativo", AssetType.Stock, null);
        investmentEvent.AffectedAssets.Add(asset);

        Assert.Equal(analysisId, investmentEvent.DocumentAnalysisId);
        Assert.Equal(InvestmentEventType.CapitalRaise, investmentEvent.EventType);
        Assert.Equal(2, investmentEvent.Version);
        Assert.Equal(2, investmentEvent.Facts.Count);
        Assert.Single(investmentEvent.AffectedAssets);
    }

    [Fact]
    public void Assessment_e_as_tres_dimensoes_sao_independentes()
    {
        var assessment = new PortfolioAssessment(Guid.NewGuid(), Guid.NewGuid(), AttentionPriority.High, ActionRequirement.Review, CapitalDecisionImpact.None, AssessmentStatus.Detected, AssessmentUserState.Unread, "Resumo", "Importa", "Revisar", IntelligenceConfidence.High, null, null);
        var other = new PortfolioAssessment(Guid.NewGuid(), Guid.NewGuid(), AttentionPriority.Medium, ActionRequirement.Monitor, CapitalDecisionImpact.Block, AssessmentStatus.Detected, AssessmentUserState.Unread, "Resumo", "Importa", "Acompanhar", IntelligenceConfidence.Medium, null, null);

        Assert.Equal(CapitalDecisionImpact.None, assessment.CapitalDecisionImpact);
        Assert.Equal(CapitalDecisionImpact.Block, other.CapitalDecisionImpact);
        Assert.NotEqual(assessment.AttentionPriority, other.AttentionPriority);
    }

    [Fact]
    public void Seen_nao_fecha_assessment_e_status_e_independente()
    {
        var assessment = new PortfolioAssessment(Guid.NewGuid(), Guid.NewGuid(), AttentionPriority.Low, ActionRequirement.None, CapitalDecisionImpact.None, AssessmentStatus.Active, AssessmentUserState.Unread, "Resumo", "Importa", "Nenhuma ação", IntelligenceConfidence.Low, 1, 1);
        assessment.MarkSeen();
        assessment.SetStatus(AssessmentStatus.Monitoring);
        Assert.Equal(AssessmentUserState.Seen, assessment.UserState);
        Assert.Equal(AssessmentStatus.Monitoring, assessment.Status);
        assessment.SetStatus(AssessmentStatus.Closed);
        Assert.Equal(AssessmentUserState.Seen, assessment.UserState);
    }

    [Fact]
    public void Assessor_contextual_registra_versoes_quando_disponiveis()
    {
        var portfolio = new Portfolio("Carteira");
        var strategy = new PortfolioAllocationStrategy(portfolio.Id, "Estratégia", null, 4, PriorityLevel.High, PriorityLevel.Medium, PriorityLevel.Medium, ContributionPolicy.CorrectImbalancesFirst, null);
        var objective = new InvestmentObjective(portfolio.Id, InvestmentGoal.PassiveIncome, TargetMetric.MonthlyPassiveIncome, 10000, TimeHorizon.LongTerm, 3);
        var document = new Document(Guid.NewGuid(), DocumentType.RelevantFact, DateTimeOffset.UtcNow, "CVM", "id", "https://example.com", "fingerprint");
        var analysis = new DocumentAnalysis(Guid.NewGuid(), document.Id, "fake", "v1", AnalysisImportance.Relevant, AnalysisClassification.CapitalRaise, false, SuggestedAction.Monitor, "Resumo", "Impacto", "Importa", new[] { "ABCD3" }, .8m, "{}");
        var investmentEvent = new InvestmentEvent(analysis.Id, InvestmentEventType.CapitalRaise, null, "Emissão", new[] { "anunciada" });
        var result = new PortfolioImpactAssessmentService().AssessPortfolioImpact(new PortfolioImpactAssessmentContext(investmentEvent, portfolio, Array.Empty<RelevantPositionContext>(), objective, strategy, null));
        Assert.Equal(InvestmentGoal.PassiveIncome, objective.PrimaryGoal);
        Assert.Equal(4, strategy.Version);
        Assert.Equal(3, objective.Version);
        Assert.Equal(ActionRequirement.Monitor, result.ActionRequirement);
    }

    [Fact]
    public void Mesmo_evento_pode_produzir_assessments_diferentes_por_carteira()
    {
        var asset = new Asset("ABCD3", "Ativo", AssetType.Stock, null);
        var eventItem = new InvestmentEvent(Guid.NewGuid(), InvestmentEventType.CorporateAction, null, "Evento corporativo", new[] { "Fato" });
        eventItem.AffectedAssets.Add(asset);
        var portfolioA = new Portfolio("A");
        var portfolioB = new Portfolio("B");
        var service = new PortfolioImpactAssessmentService();

        var a = service.AssessPortfolioImpact(new PortfolioImpactAssessmentContext(eventItem, portfolioA,
            new[] { new RelevantPositionContext(asset.Id, asset.Ticker, asset.AssetClass, 1000, 5) }, null, null, null));
        var b = service.AssessPortfolioImpact(new PortfolioImpactAssessmentContext(eventItem, portfolioB,
            new[] { new RelevantPositionContext(asset.Id, asset.Ticker, asset.AssetClass, 1000, 25) }, null, null, null));

        Assert.NotEqual(a.AttentionPriority, b.AttentionPriority);
        Assert.NotEqual(a.ActionRequirement, b.ActionRequirement);
        Assert.Equal(CapitalDecisionImpact.Caution, a.CapitalDecisionImpact);
        Assert.Equal(CapitalDecisionImpact.Caution, b.CapitalDecisionImpact);
    }

    [Fact]
    public void SuggestedAction_legado_nao_determina_impacto_de_capital()
    {
        var eventItem = new InvestmentEvent(Guid.NewGuid(), InvestmentEventType.Governance, null, "Governança", new[] { "Fato" });
        var portfolio = new Portfolio("Carteira");
        var output = new PortfolioImpactAssessmentService().AssessPortfolioImpact(new PortfolioImpactAssessmentContext(eventItem, portfolio, Array.Empty<RelevantPositionContext>(), null, null, null));
        Assert.Equal(CapitalDecisionImpact.None, output.CapitalDecisionImpact);
    }

    [Fact]
    public void Identidade_sem_strategy_e_objective_normaliza_nulls_para_mesma_chave()
    {
        var portfolioId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var first = CreateAssessment(portfolioId, eventId, null, null, "portfolio-impact-v1");
        var second = CreateAssessment(portfolioId, eventId, null, null, "portfolio-impact-v1");

        Assert.Equal(first.StrategyVersionKey, second.StrategyVersionKey);
        Assert.Equal(first.ObjectiveVersionKey, second.ObjectiveVersionKey);
        Assert.Equal(0, first.StrategyVersionKey);
        Assert.Equal(0, first.ObjectiveVersionKey);
    }

    [Fact]
    public void Mudancas_de_versao_produzem_chaves_de_reassessment_diferentes()
    {
        var portfolioId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var baseline = CreateAssessment(portfolioId, eventId, 1, 1, "portfolio-impact-v1");
        var strategyChanged = CreateAssessment(portfolioId, eventId, 2, 1, "portfolio-impact-v1");
        var objectiveChanged = CreateAssessment(portfolioId, eventId, 1, 2, "portfolio-impact-v1");
        var assessorChanged = CreateAssessment(portfolioId, eventId, 1, 1, "portfolio-impact-v2");

        Assert.NotEqual(baseline.StrategyVersionKey, strategyChanged.StrategyVersionKey);
        Assert.NotEqual(baseline.ObjectiveVersionKey, objectiveChanged.ObjectiveVersionKey);
        Assert.NotEqual(baseline.AssessmentVersion, assessorChanged.AssessmentVersion);
    }

    [Fact]
    public void Nova_versao_do_evento_tem_novo_id_e_pode_gerar_novo_assessment()
    {
        var analysisId = Guid.NewGuid();
        var firstEvent = new InvestmentEvent(analysisId, InvestmentEventType.Other, null, "Fato v1", new[] { "v1" }, 1);
        var secondEvent = new InvestmentEvent(analysisId, InvestmentEventType.Other, null, "Fato v2", new[] { "v2" }, 2);

        Assert.NotEqual(firstEvent.Id, secondEvent.Id);
        Assert.NotEqual(firstEvent.Version, secondEvent.Version);
    }

    private static PortfolioAssessment CreateAssessment(Guid portfolioId, Guid eventId, int? strategyVersion, int? objectiveVersion, string assessmentVersion)
        => new(portfolioId, eventId, AttentionPriority.Medium, ActionRequirement.Monitor, CapitalDecisionImpact.None, AssessmentStatus.Detected, AssessmentUserState.Unread, "Resumo", "Importa", "Acompanhar", IntelligenceConfidence.Medium, strategyVersion, objectiveVersion, assessmentVersion);

    [Theory]
    [InlineData(AttentionPriority.High, ActionRequirement.None, CapitalDecisionImpact.None, true)]
    [InlineData(AttentionPriority.Low, ActionRequirement.Review, CapitalDecisionImpact.None, true)]
    [InlineData(AttentionPriority.Medium, ActionRequirement.Monitor, CapitalDecisionImpact.Block, true)]
    [InlineData(AttentionPriority.Low, ActionRequirement.None, CapitalDecisionImpact.None, false)]
    public void AttentionNow_eh_composicao_independente(AttentionPriority priority, ActionRequirement action, CapitalDecisionImpact impact, bool expected)
        => Assert.Equal(expected, RadarAttentionNowPolicy.RequiresAttention(AssessmentStatus.Active, priority, action, impact));

    [Fact]
    public void Assessment_fechado_nunca_aparece_em_AttentionNow()
        => Assert.False(RadarAttentionNowPolicy.RequiresAttention(AssessmentStatus.Closed, AttentionPriority.Critical, ActionRequirement.Act, CapitalDecisionImpact.Block));
}
