using Prumo.Application.Documents;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class DocumentClassificationPolicyTests
{
    [Theory]
    [InlineData("Aviso aos Acionistas", null, null, true)]
    [InlineData("  AVISO   AOS ACIONISTAS ", null, null, true)]
    [InlineData(null, "Avisos aos Acionistas", null, true)]
    [InlineData(null, null, "Aviso aos Acionistas", true)]
    [InlineData("Comunicado ao Mercado", "Dividendos", null, false)]
    [InlineData("Fato Relevante", "JCP", null, false)]
    [InlineData("Aviso aos Cotistas - Estruturado", "Rendimentos e Amortizações", null, false)]
    [InlineData(null, null, null, false)]
    public void Identifies_shareholder_notices_from_official_metadata(string? category, string? type, string? species, bool expected)
        => Assert.Equal(expected, DocumentClassificationPolicy.IsShareholderNotice(category, type, species));

    [Theory]
    [InlineData("Relatórios", "Relatório Gerencial", null, DocumentType.ManagementReport)]
    [InlineData("Informes Periódicos", "Informe Mensal", null, DocumentType.MonthlyReport)]
    [InlineData("Informes Periódicos", "Informe Trimestral", null, DocumentType.QuarterlyReport)]
    [InlineData("Fatos Relevantes", "Fato Relevante", null, DocumentType.RelevantFact)]
    [InlineData("Comunicado ao Mercado", "Outros Comunicados Não Considerados Fatos Relevantes", null, DocumentType.Communication)]
    [InlineData("Comunicados ao Mercado", "Apresentações a analistas/agentes do mercado", null, DocumentType.Communication)]
    [InlineData("Aviso aos Cotistas - Estruturado", "Rendimentos e Amortizações", null, DocumentType.Other)]
    public void Maps_official_metadata_deterministically(string category, string type, string? species, DocumentType expected)
    {
        Assert.Equal(expected, DocumentClassificationPolicy.Map(category, type, species));
    }

    [Fact]
    public void Unknown_type_remains_unknown_without_inference()
    {
        Assert.Equal(DocumentType.Other, DocumentClassificationPolicy.Map("Relatórios", "Anúncio de Encerramento", null));
    }

    [Theory]
    [InlineData("Anúncio de Início")]
    [InlineData("Instrumento Particular de Emissão de Cotas")]
    [InlineData("Anúncio de Encerramento")]
    [InlineData("Instrumento Particular de Emissão de Novas Cotas")]
    [InlineData("Prospecto Definitivo de Distribuição Pública")]
    [InlineData("Prospecto Preliminar")]
    [InlineData("Lâmina da Oferta")]
    [InlineData("Material Publicitário")]
    [InlineData("Boletim de Subscrição")]
    [InlineData("Aviso ao Mercado")]
    public void Identifies_operational_issuance_documents(string sourceType)
    {
        Assert.True(DocumentClassificationPolicy.IsOperationalIssuanceDocument(null, sourceType, null));
    }

    [Fact]
    public void Does_not_skip_relevant_fact_with_issuance_wording()
    {
        Assert.False(DocumentClassificationPolicy.IsOperationalIssuanceDocument("Fatos Relevantes", "Fato Relevante", null));
    }

    [Theory]
    [InlineData("Assembleia", "AGE", "Mapa final de votação detalhado", true)]
    [InlineData("Assembleia", "AGO", "Mapa final de votação sintético", true)]
    [InlineData("Assembleia", "AGE", "Mapa consolidado de voto a distância", true)]
    [InlineData("Assembleia", "AGE", "Boletim de voto a distância", true)]
    [InlineData(null, "  MAPA   FINAL DE VOTAÇÃO DETALHADO ", null, true)]
    [InlineData("Ofertas Públicas", "Anúncio de Início", null, true)]
    [InlineData("Ofertas Públicas", "Anúncio de Encerramento", null, true)]
    [InlineData("  EMISSÃO   DE NOVAS COTAS ", null, null, true)]
    [InlineData(null, null, "Lâmina de Oferta", true)]
    [InlineData("Fato Relevante", "Anúncio de Início", null, false)]
    [InlineData("Fato Relevante", null, "Mapa final de votação detalhado", false)]
    [InlineData("Assembleia", "AGE", "Ata", false)]
    [InlineData("Reunião da Administração", "Conselho de Administração", "Ata", false)]
    [InlineData("Estatuto Social", null, null, false)]
    [InlineData("Aviso aos Debenturistas", null, null, false)]
    [InlineData("Comunicado ao Mercado", "Outros Comunicados Não Considerados Fatos Relevantes", null, false)]
    [InlineData("Dados Econômico-Financeiros", "Demonstrações Financeiras Intermediárias", null, false)]
    [InlineData("Relatórios", "Relatório Gerencial", null, false)]
    [InlineData(null, null, null, false)]
    public void Skips_only_known_operational_metadata(string? category, string? type, string? species, bool ignored)
        => Assert.Equal(ignored, DocumentClassificationPolicy.GetAutomaticAnalysisIgnoredReason(category, type, species) is not null);
}
