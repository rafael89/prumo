using Lodira.Application.Documents;

namespace Lodira.Domain.Tests;

public sealed class TextQualityValidatorTests
{
    private readonly TextQualityValidator validator = new(new TextQualityOptions { MinimumCharacters = 20, MinimumPrintableRatio = 0.7m });

    [Fact]
    public void Rejeita_texto_vazio()
    {
        Assert.False(validator.Validate(string.Empty).IsValid);
    }

    [Fact]
    public void Rejeita_texto_insuficiente()
    {
        var result = validator.Validate("texto curto");

        Assert.False(result.IsValid);
        Assert.Equal("Texto insuficiente para análise.", result.Error);
    }

    [Fact]
    public void Aceita_texto_com_quantidade_minima_de_caracteres()
    {
        var result = validator.Validate("Este documento contem texto suficiente para passar pela validacao minima.");

        Assert.True(result.IsValid);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Rejeita_texto_com_caracteres_invalidos()
    {
        var result = validator.Validate("texto valido" + new string('\u0001', 20) + "com ruido");

        Assert.False(result.IsValid);
        Assert.Equal("Texto possui caracteres inválidos em excesso.", result.Error);
    }
}
