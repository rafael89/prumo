using Prumo.Application.Abstractions;
using Prumo.Application.Documents;

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
