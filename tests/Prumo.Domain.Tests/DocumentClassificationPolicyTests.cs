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
    public void Identifies_operational_issuance_documents(string sourceType)
    {
        Assert.True(DocumentClassificationPolicy.IsOperationalIssuanceDocument(null, sourceType, null));
    }

    [Fact]
    public void Does_not_skip_relevant_fact_with_issuance_wording()
    {
        Assert.False(DocumentClassificationPolicy.IsOperationalIssuanceDocument("Fatos Relevantes", "Fato Relevante", null));
    }
}
