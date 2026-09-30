using Prumo.Application.Documents;

namespace Prumo.Domain.Tests;

public sealed class FiiDistributionNoticeParserTests
{
    [Fact]
    public void Parses_official_fnet_notice_fields_without_confusing_base_and_payment_dates()
    {
        const string text = """
            Código de negociação: CPTS11
            Data-base (último dia de negociação “com” direito ao provento)  | 15/09/2026  |
            Valor do provento (R$/unidade)  | 0,09  |
            Data do pagamento  | 22/09/2026  |
            """;

        var notice = FiiDistributionNoticeParser.Parse(text);

        Assert.NotNull(notice);
        Assert.Equal(0.09m, notice.AmountPerUnit);
        Assert.Equal(new DateOnly(2026, 9, 22), notice.PaymentDate);
        Assert.Equal(new DateOnly(2026, 9, 15), notice.BaseDate);
        Assert.True(FiiDistributionNoticeParser.IsNotice("Aviso aos Cotistas - Estruturado", "Rendimentos e Amortizações"));
    }

    [Fact]
    public void Parses_extracted_xml_field_names()
    {
        const string text = "DataBase: 2026-09-15\nValorProvento: 0.0900\nDataPagamento: 2026-09-22";

        var notice = FiiDistributionNoticeParser.Parse(text);

        Assert.NotNull(notice);
        Assert.Equal(0.09m, notice.AmountPerUnit);
        Assert.Equal(new DateOnly(2026, 9, 22), notice.PaymentDate);
    }

    [Fact]
    public void Rejects_ambiguous_values_or_missing_payment_date()
    {
        Assert.Null(FiiDistributionNoticeParser.Parse("Valor do provento (R$/unidade): 0,09"));
        Assert.Null(FiiDistributionNoticeParser.Parse("ValorProvento: 0,09\nValorProvento: 0,12\nDataPagamento: 2026-09-22"));
    }

    [Theory]
    [InlineData("Rendimentos no valor de R$ 0,81 por cota, com pagamento programado para 25 de setembro de 2026.", 0.81, 25)]
    [InlineData("Rendimentos no valor de 0,09 por cota; pagamento previsto para 22 de setembro de 2026.", 0.09, 22)]
    [InlineData("Rendimentos de 0.35 por cota. Data de pagamento é 25/09/2026.", 0.35, 25)]
    [InlineData("Rendimentos de R$ 0,92 por cota. Pagamento será realizado em 2026-09-25.", 0.92, 25)]
    public void Parses_persisted_analysis_summary(string summary, decimal amount, int day)
    {
        var notice = FiiDistributionNoticeParser.ParseAnalysisSummary(summary);
        Assert.NotNull(notice);
        Assert.Equal(amount, notice.AmountPerUnit);
        Assert.Equal(new DateOnly(2026, 9, day), notice.PaymentDate);
    }

    [Fact]
    public void Analysis_summary_requires_a_single_amount_and_payment_date()
    {
        Assert.Null(FiiDistributionNoticeParser.ParseAnalysisSummary("R$ 0,81 por cota, sem data de pagamento."));
        Assert.Null(FiiDistributionNoticeParser.ParseAnalysisSummary("R$ 0,81 por cota antes e R$ 0,90 por cota agora. Pagamento em 25/09/2026."));
    }

    [Fact]
    public void Analysis_summary_retains_the_base_date_for_revision_matching()
    {
        var notice = FiiDistributionNoticeParser.ParseAnalysisSummary(
            "R$ 1,00 por cota, com data-base em 15 de setembro de 2026 e pagamento em 22 de setembro de 2026.");

        Assert.NotNull(notice);
        Assert.Equal(new DateOnly(2026, 9, 15), notice.BaseDate);
        Assert.Equal(new DateOnly(2026, 9, 22), notice.PaymentDate);
    }
}
