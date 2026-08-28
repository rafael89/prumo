using Prumo.Application.Abstractions;
using Prumo.Application.Documents;

namespace Prumo.Domain.Tests;

public sealed class DocumentAnalysisPromptTests
{
    [Fact]
    public void FormataPesoComDuasCasasDecimaisEmPortugues()
    {
        var context = CreateContext(69.7471665213600697471665213m);

        var prompt = DocumentAnalysisPrompt.Build("texto", context);

        Assert.Contains("\"portfolioWeight\":\"69,75%\"", prompt);
        Assert.DoesNotContain("69.7471665213600697471665213", prompt);
    }

    [Fact]
    public void FormataPercentualSimplesComDuasCasas()
    {
        var context = CreateContext(10m);

        var prompt = DocumentAnalysisPrompt.Build("texto", context);

        Assert.Contains("\"portfolioWeight\":\"10,00%\"", prompt);
    }

    [Fact]
    public void NaoAlteraValorInternoDoContexto()
    {
        var context = CreateContext(69.7471665213600697471665213m);

        _ = DocumentAnalysisPrompt.Build("texto", context);

        Assert.Equal(69.7471665213600697471665213m, context.PortfolioWeight);
    }

    private static PortfolioAnalysisContext CreateContext(decimal portfolioWeight) => new(
        "XPLG11", "Fii", "Logística", 10m, 100m, 1100m, portfolioWeight);
}
