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
}
