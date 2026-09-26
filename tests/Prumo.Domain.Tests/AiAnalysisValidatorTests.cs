using Prumo.Application.Documents;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class AiAnalysisValidatorTests
{
    private readonly AiAnalysisValidator validator = new();

    [Fact]
    public void Aceita_json_valido()
    {
        var result = validator.Validate(CreatePayload(), """{"importance":"Critical"}""");

        Assert.Equal("Resumo objetivo", result.Summary);
        Assert.Equal(0.92m, result.Confidence);
        Assert.Equal(new[] { "XPIN11" }, result.AffectedAssets);
    }

    [Fact]
    public void Rejeita_enum_invalido()
    {
        var payload = CreatePayload() with { Classification = "Desconhecida" };

        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate(payload, "{}"));

        Assert.Contains("Classification", exception.Message);
    }

    [Fact]
    public void Rejeita_confidence_menor_que_zero()
    {
        var payload = CreatePayload() with { Confidence = -0.1m };

        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate(payload, "{}"));

        Assert.Contains("Confidence", exception.Message);
    }

    [Fact]
    public void Rejeita_confidence_maior_que_um()
    {
        var payload = CreatePayload() with { Confidence = 1.1m };

        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate(payload, "{}"));

        Assert.Contains("Confidence", exception.Message);
    }

    [Fact]
    public void Rejeita_campo_obrigatorio_ausente()
    {
        var payload = CreatePayload() with { Summary = null };

        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate(payload, "{}"));

        Assert.Contains("Summary", exception.Message);
    }

    [Fact]
    public void Rejeita_summary_vazio()
    {
        var payload = CreatePayload() with { Summary = "   " };

        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate(payload, "{}"));

        Assert.Contains("Summary", exception.Message);
    }

    [Fact]
    public void Rejeita_json_sem_lista_de_ativos()
    {
        var payload = CreatePayload() with { AffectedAssets = null };

        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate(payload, "{}"));

        Assert.Contains("AffectedAssets", exception.Message);
    }

    [Fact]
    public void Normaliza_occurred_at_para_utc()
    {
        var payload = CreatePayload() with
        {
            Events = new[]
            {
                new AiInvestmentEventPayload("FinancialResult", DateTimeOffset.Parse("2026-08-29T10:00:00-03:00"), "Resultado", Array.Empty<string>(), Array.Empty<string>())
            }
        };

        var result = validator.Validate(payload, "{}");

        Assert.NotNull(result.Events);
        Assert.Equal(TimeSpan.Zero, result.Events[0].OccurredAt!.Value.Offset);
        Assert.Equal(DateTimeOffset.Parse("2026-08-29T13:00:00Z"), result.Events[0].OccurredAt);
    }

    [Fact]
    public void Rebaixa_pagamento_rotineiro_para_informativo_e_sem_acao()
    {
        var payload = CreatePayload() with
        {
            Importance = "Critical",
            Classification = "Distribution",
            RequiresAction = true,
            SuggestedAction = "Monitor",
            Summary = "Pagamento de rendimentos referente ao mês de julho.",
            Impact = "Distribuição de R$ 0,85 por cota aos investidores."
        };

        var result = validator.Validate(payload, "{}");

        Assert.Equal(AnalysisImportance.Informative, result.Importance);
        Assert.False(result.RequiresAction);
        Assert.Equal(SuggestedAction.NoAction, result.SuggestedAction);
    }

    [Fact]
    public void Preserva_relevancia_de_distribuicao_com_excecao_material()
    {
        var payload = CreatePayload() with
        {
            Importance = "Relevant",
            Classification = "Distribution",
            Summary = "Suspensão temporária da distribuição de rendimentos por inadimplência."
        };

        var result = validator.Validate(payload, "{}");

        Assert.Equal(AnalysisImportance.Relevant, result.Importance);
    }

    private static AiAnalysisPayload CreatePayload()
    {
        return new AiAnalysisPayload(
            "Critical",
            "Liquidation",
            true,
            "Monitor",
            "Resumo objetivo",
            "Impacto material para o ativo.",
            "Impacto considerando a carteira consolidada.",
            new[] { "xpin11", "XPIN11" },
            0.92m);
    }
}
