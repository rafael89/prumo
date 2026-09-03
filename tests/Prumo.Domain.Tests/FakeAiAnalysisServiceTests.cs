using Microsoft.Extensions.Logging.Abstractions;
using Prumo.Application.Abstractions;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Ai;

namespace Prumo.Domain.Tests;

public sealed class FakeAiAnalysisServiceTests
{
    [Fact]
    public async Task RetornaResultadoDeterministicoComTickerDoContexto()
    {
        var service = new FakeAiAnalysisService();
        var context = new PortfolioAnalysisContext("XPLG11", "Fii", "Logística", 10, 100, 1100, 25);

        var result = await service.AnalyzeAsync("texto", context);

        Assert.Equal("fake-e2e", service.Model);
        Assert.Equal(AnalysisImportance.Informative, result.Importance);
        Assert.Equal(AnalysisClassification.Other, result.Classification);
        Assert.False(result.RequiresAction);
        Assert.Equal(SuggestedAction.NoAction, result.SuggestedAction);
        Assert.Equal("Documento processado com sucesso.", result.Summary);
        Assert.Equal(new[] { "XPLG11" }, result.AffectedAssets);
        Assert.Equal(1.0m, result.Confidence);
        Assert.Contains("XPLG11", result.RawResponse);
    }
}
