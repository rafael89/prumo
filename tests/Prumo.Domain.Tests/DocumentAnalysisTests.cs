using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class DocumentAnalysisTests
{
    [Fact]
    public void Rejeita_confianca_fora_do_intervalo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateAnalysis(1.01m));
    }

    [Fact]
    public void Normaliza_e_deduplica_ativos_afetados()
    {
        var analysis = CreateAnalysis(0.92m, new[] { "hglg11", "HGLG11", "lvbi11" });

        Assert.Equal(new[] { "HGLG11", "LVBI11" }, analysis.AffectedAssets);
    }

    private static DocumentAnalysis CreateAnalysis(decimal confidence, IEnumerable<string>? affectedAssets = null)
    {
        return new DocumentAnalysis(Guid.NewGuid(), Guid.NewGuid(), "fake-model", "document-analysis-v1", AnalysisImportance.Relevant, AnalysisClassification.CorporateAction, true, SuggestedAction.Monitor, "Resumo", "Impacto", "Impacto na carteira", affectedAssets ?? new[] { "HGLG11" }, confidence, "{}");
    }
}
