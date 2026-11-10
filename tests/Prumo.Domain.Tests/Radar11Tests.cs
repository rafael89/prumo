using Prumo.Application.Abstractions;
using Prumo.Application.Documents;
using Prumo.Application.Intelligence;
using Prumo.Application.Radar;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class Radar11Tests
{
    private static readonly Guid AssetId = Guid.NewGuid();

    [Fact]
    public void Evidence_reference_does_not_split_a_surrogate_pair_at_the_storage_limit()
    {
        var prefix = new string('x', InvestmentEventEvidence.MaxReferenceLength - 1);
        var evidence = new InvestmentEventEvidence(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), prefix + "😀continuação");
        Assert.Equal(prefix, evidence.Reference);
    }

    [Theory]
    [InlineData(InvestmentEventType.Liquidation, "processo de liquidação 77/2026", "anunciada", "em andamento")]
    [InlineData(InvestmentEventType.FinancialResult, "processo 123/2026", "inadimplência", "recuperação parcial")]
    [InlineData(InvestmentEventType.AssetTransaction, "contrato 55/2026", "contratada", "concluída")]
    public void Concrete_stage_change_is_update(InvestmentEventType type, string id, string before, string after)
    {
        var first = Event(Details(id, before), type);
        var next = RadarEventPolicy.Classify(Details(id, after), type, [AssetId], [first]);
        Assert.Equal("Update", next.Novelty);
        Assert.Equal(first.RadarDetails!.ContinuityKey, next.ContinuityKey);
        Assert.Equal(first.Id, next.PreviousEventId);
    }

    [Fact]
    public void Liquidation_deadline_changes_without_creating_a_new_identity()
    {
        var first = Event(Details("processo de liquidação 77/2026", "anunciada"), InvestmentEventType.Liquidation);
        var second = Event(Details("processo de liquidação 77/2026", "em andamento", "20/10/2026"), InvestmentEventType.Liquidation, [first]);
        var third = RadarEventPolicy.Classify(Details("processo de liquidação 77/2026", "em andamento", "30/10/2026"), InvestmentEventType.Liquidation, [AssetId], [first, second]);
        Assert.Equal("Update", third.Novelty);
        Assert.Equal(second.Id, third.PreviousEventId);
    }

    [Theory]
    [InlineData(InvestmentEventType.CapitalRaise, "8ª emissão", "encerrada", ActionRequirement.None, "sem alerta no Radar")]
    [InlineData(InvestmentEventType.Governance, "instrumento 32/2026", "administrador substituído", ActionRequirement.Monitor, "sem novo alerta forte")]
    public void Repeated_economic_state_does_not_create_another_strong_alert(InvestmentEventType type, string id, string stage, ActionRequirement expectedAction, string expectedReason)
    {
        var first = Event(Details(id, stage), type);
        var details = RadarEventPolicy.Classify(Details(id, stage), type, [AssetId], [first]);
        Assert.Equal("Repeat", details.Novelty);
        var repeat = new InvestmentEvent(Guid.NewGuid(), type, null, "Apresentação ou XML posterior", []);
        repeat.SetRadarDetails(details);
        var output = Assess(repeat, 80);
        Assert.Equal(AttentionPriority.Low, output.AttentionPriority);
        Assert.Equal(expectedAction, output.ActionRequirement);
        Assert.Equal(CapitalDecisionImpact.None, output.CapitalDecisionImpact);
        Assert.Contains(expectedReason, output.WhyItMatters);
    }

    [Fact]
    public void Missing_or_different_evidence_cannot_establish_repeat_or_update()
    {
        var first = Event(Details("contrato 55/2026", "contratada"));
        var missing = Details("contrato 55/2026", "contratada") with { Stage = null };
        Assert.Equal("Unknown", RadarEventPolicy.Classify(missing, first.EventType, [AssetId], [first]).Novelty);
        var reworded = Details("contrato 55/2026", "assinada") with { AttentionBasis = "UNKNOWN", AttentionEvidence = null };
        Assert.Equal("Unknown", RadarEventPolicy.Classify(reworded, first.EventType, [AssetId], [first]).Novelty);
        Assert.Equal("Unknown", RadarEventPolicy.Classify(new(), first.EventType, [AssetId], [first]).Novelty);
    }

    [Fact]
    public void Amount_and_counterparty_alone_do_not_link_transactions()
    {
        var first = Event(Details("contrato 55/2026", "contratada"));
        var unknown = RadarEventPolicy.Validate(new(Amount: "100 milhões", Evidence: "100 milhões, Empresa X"), "100 milhões, Empresa X", AnalysisImportance.Relevant);
        Assert.Equal("Unknown", RadarEventPolicy.Classify(unknown, first.EventType, [AssetId], [first]).Novelty);
        Assert.Equal("New", RadarEventPolicy.Classify(Details("contrato 56/2026", "contratada"), first.EventType, [AssetId], [first]).Novelty);
        Assert.Equal("New", RadarEventPolicy.Classify(first.RadarDetails! with { ContinuityKey = null }, InvestmentEventType.CapitalRaise, [AssetId], [first]).Novelty);
    }

    [Theory]
    [InlineData("R$ 100 milhões")]
    [InlineData("Empresa X")]
    [InlineData("Edifício 123")]
    public void A_literal_amount_or_name_is_not_an_operation_identifier(string value)
        => Assert.Null(RadarEventPolicy.Validate(new(OperationId: value, Evidence: value), value, AnalysisImportance.Relevant).OperationId);

    [Fact]
    public void Consecutive_distributions_with_same_amount_remain_separate()
    {
        var july = Event(Details("distribuição julho 2026", "R$ 1,00"), InvestmentEventType.Distribution);
        var august = RadarEventPolicy.Classify(Details("distribuição agosto 2026", "R$ 1,00"), InvestmentEventType.Distribution, [AssetId], [july]);
        Assert.Equal("Unknown", august.Novelty);
        Assert.Null(august.ContinuityKey);
    }

    [Fact]
    public void Partial_recovery_keeps_outstanding_debt_and_mixed_direction()
    {
        const string source = "processo 123; recuperação parcial; R$ 2 milhões recuperados; R$ 8 milhões pendentes";
        var details = RadarEventPolicy.Validate(new(OperationId: "processo 123", Stage: "recuperação parcial", Amount: "R$ 2 milhões", OutstandingDebt: "R$ 8 milhões", Evidence: source, ImpactDirection: "Mixed"), source, AnalysisImportance.Relevant);
        Assert.Equal("Mixed", details.ImpactDirection);
        Assert.Equal("R$ 8 milhões", details.OutstandingDebt);
        Assert.Equal("Unknown", details.Materiality);
    }

    [Theory]
    [InlineData("imóvel")]
    [InlineData("participação societária")]
    [InlineData("concessão")]
    public void Material_transaction_needs_no_property_name(string asset)
    {
        var e = Event(Details("contrato 55", $"aquisição de {asset}"));
        var output = Assess(e, 6);
        Assert.Equal(AnalysisImportance.Relevant, RadarEventPolicy.Importance(e));
        Assert.Equal(ActionRequirement.Review, output.ActionRequirement);
        Assert.Equal("Indeterminate", e.RadarDetails!.ImpactDirection);
    }

    [Fact]
    public void Portfolio_exposure_never_changes_global_relevance_and_routine_stays_low()
    {
        var e = Event(Details("instrumento 32", "troca administrativa") with { Importance = "Informative", ImpactDirection = "Neutral" }, InvestmentEventType.Governance);
        Assert.Equal(AttentionPriority.Low, Assess(e, 90).AttentionPriority);
        Assert.Equal(ActionRequirement.None, Assess(e, 90).ActionRequirement);
        Assert.Equal("Informative", e.RadarDetails!.Importance);
        Assert.Equal(AnalysisImportance.Informative, RadarEventPolicy.Importance(e));
    }

    [Fact]
    public void Critical_requires_both_literal_consequence_and_urgency()
    {
        const string source = "Perda integral. Decisão obrigatória imediata.";
        var details = new RadarEventDetails(Importance: "Critical", ConsequenceEvidence: "Perda integral.");
        Assert.Equal("Relevant", RadarEventPolicy.Validate(details, source, AnalysisImportance.Critical).Importance);
        Assert.Equal("Relevant", RadarEventPolicy.Validate(details with { UrgencyEvidence = "prazo inventado" }, source, AnalysisImportance.Critical).Importance);
        Assert.Equal("Critical", RadarEventPolicy.Validate(details with { UrgencyEvidence = "Decisão obrigatória imediata." }, source, AnalysisImportance.Critical).Importance);
    }

    [Fact]
    public void Unsupported_secondary_fields_are_isolated_and_server_fields_cannot_be_injected()
    {
        var d = RadarEventPolicy.Validate(new(OperationId: "contrato 55", Stage: "concluída", Amount: "999 bilhões", Evidence: "contrato 55 concluída", Novelty: "Repeat", ContinuityKey: "injetada", Materiality: "90%"), "contrato 55 concluída", AnalysisImportance.Relevant);
        Assert.Equal("concluída", d.Stage);
        Assert.Null(d.Amount);
        Assert.Equal("Unknown", d.Novelty);
        Assert.Null(d.ContinuityKey);
        Assert.Equal("Unknown", d.Materiality);
    }

    [Fact]
    public void Invalid_secondary_event_does_not_destroy_a_valid_transaction()
    {
        var payload = new AiAnalysisPayload("Relevant", "AssetTransaction", false, "Monitor", "Aquisição e emissão", "Impacto", null, ["TEST11"], 0.9m,
            [new("AssetTransaction", null, "Aquisição", [], ["TEST11"]), new("invalid", null, "Evento inválido", [], []), null!],
            FinancialFacts: [null!]);
        Assert.Single(new AiAnalysisValidator().Validate(payload, "{}").Events!);
    }

    private static RadarEventDetails Details(string id, string stage, string? deadline = null)
    {
        var source = $"{id}; {stage}; {deadline}";
        return RadarEventPolicy.Validate(new(OperationId: id, Stage: stage, Deadline: deadline, Evidence: source,
            AttentionBasis: "CHANGE", AttentionReason: "Mudança explícita", AttentionEvidence: source), source, AnalysisImportance.Relevant);
    }

    private static InvestmentEvent Event(RadarEventDetails details, InvestmentEventType type = InvestmentEventType.AssetTransaction, IReadOnlyList<InvestmentEvent>? recent = null)
    {
        var e = new InvestmentEvent(Guid.NewGuid(), type, null, "Evento", []);
        e.SetRadarDetails(RadarEventPolicy.Classify(details, type, [AssetId], recent ?? []));
        return e;
    }

    private static PortfolioImpactAssessmentOutput Assess(InvestmentEvent e, decimal exposure)
        => new PortfolioImpactAssessmentService().AssessPortfolioImpact(new(e, new Portfolio("Teste"),
            [new RelevantPositionContext(AssetId, "TEST11", AssetClass.Fii, 100, exposure)], null,
            AnalysisImportance.Relevant, false, SuggestedAction.Monitor));
}
