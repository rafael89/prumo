using Prumo.Application.Documents;
using Prumo.Infrastructure.Documents;

namespace Prumo.Domain.Tests;

public sealed class StockDividendNoticeParserTests
{
    [Fact]
    public void Reads_dividends_and_net_jcp_with_a_shared_payment_date()
    {
        var notices = StockDividendNoticeParser.ParseAll("Dividendos: R$ 1,00 por ação. JCP: valor bruto por ação R$ 0,50. Valor líquido por ação R$ 0,425. Data-base: 01/09/2026. Pagamento em 20/09/2026.");

        Assert.Equal(2, notices.Count);
        Assert.Equal(StockDistributionType.Dividend, notices[0].Type);
        Assert.Equal(1m, notices[0].AmountPerShare);
        Assert.Equal(StockDistributionType.Jcp, notices[1].Type);
        Assert.Equal(0.425m, notices[1].AmountPerShare);
        Assert.Equal(StockDistributionAmountBasis.Net, notices[1].AmountBasis);
        Assert.Equal(0.5m, notices[1].GrossAmountPerShare);
        Assert.All(notices, x => Assert.Equal(new DateOnly(2026, 9, 20), x.PaymentDate));
        Assert.All(notices, x => Assert.Equal(new DateOnly(2026, 9, 1), x.BaseDate));
    }

    [Fact]
    public void Keeps_each_distribution_with_its_own_payment_date()
    {
        var notices = StockDividendNoticeParser.ParseAll("Dividendos: R$ 1,00 por ação. Pagamento em 10/09/2026. JCP bruto: R$ 0,50 por ação. Pagamento em 20/09/2026.");

        Assert.Equal(2, notices.Count);
        Assert.Equal(new DateOnly(2026, 9, 10), notices[0].PaymentDate);
        Assert.Equal(new DateOnly(2026, 9, 20), notices[1].PaymentDate);
        Assert.Equal(StockDistributionAmountBasis.Gross, notices[1].AmountBasis);
    }

    [Theory]
    [InlineData("Dividendos: R$ 1,98 por ação antes, R$ 2,10 por ação agora. Pagamento em 03/09/2026.")]
    [InlineData("JCP: R$ 0,50 por ação. Data-base: 03/09/2026.")]
    [InlineData("Dividendos: R$ 1,00 por ação. Pagamento em 10/09/2026 ou pagamento em 20/09/2026.")]
    public void Parse_all_rejects_ambiguous_or_missing_payment_evidence(string text)
    {
        Assert.Empty(StockDividendNoticeParser.ParseAll(text));
    }

    [Fact]
    public void Reads_precise_amount_per_share_and_payment_date()
    {
        const string text = """
            BB SEGURIDADE PARTICIPAÇÕES S.A. comunica dividendos intercalares.
            Valor dos dividendos: R$ 3.850.048.477,61.
            O valor por ação é R$ 1,98328466981.
            Data-base: 06.08.2026.
            Data de pagamento: 03.09.2026.
            """;

        var notice = StockDividendNoticeParser.Parse(text);

        Assert.NotNull(notice);
        Assert.Equal(1.98328466981m, notice.AmountPerShare);
        Assert.Equal(new DateOnly(2026, 9, 3), notice.PaymentDate);
    }

    [Fact]
    public void Does_not_use_total_amount_or_date_base_as_payment()
    {
        Assert.Null(StockDividendNoticeParser.Parse("Dividendos de R$ 3.850.048.477,61. Data-base: 06.08.2026."));
        Assert.Null(StockDividendNoticeParser.Parse("Dividendos de R$ 1,98 por ação. Data-base: 06.08.2026."));
    }

    [Fact]
    public void Rejects_two_different_per_share_amounts()
    {
        Assert.Null(StockDividendNoticeParser.Parse("Dividendos: R$ 1,98 por ação antes, R$ 2,10 por ação agora. Data de pagamento: 03.09.2026."));
    }

    [Fact]
    public void Reads_bbse3_style_table_with_headers_before_value_and_date()
    {
        const string text = """
            AVISO AOS ACIONISTAS
            A BB Seguridade comunica dividendos intercalares no montante de R$3.850.048.477,61.
            Ações
            BBSE3
            Dividendo
            por ação
            Data do
            Pagamento
            Dividendos
            R$ 1,98328466981
            03.09.2026
            Farão jus os acionistas com posição em 06.08.2026.
            """;

        var notice = StockDividendNoticeParser.Parse(text);

        Assert.NotNull(notice);
        Assert.Equal(1.98328466981m, notice.AmountPerShare);
        Assert.Equal(new DateOnly(2026, 9, 3), notice.PaymentDate);
    }

    [Fact]
    public async Task Reads_official_html_table_through_document_extractor()
    {
        const string html = """
            <html><body><h1>BB Seguridade Participações S.A.</h1><p>CNPJ 17.344.597/0001-94</p>
            <p>Comunicado aos acionistas: dividendos intercalares referentes ao primeiro semestre.</p>
            <table><tr><td>Valor por ação</td><td>R$ 1,98328466981</td></tr>
            <tr><td>Data de pagamento</td><td>03.09.2026</td></tr></table></body></html>
            """;
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html));

        var extraction = await new PdfPigTextExtractor().ExtractAsync(stream, TestContext.Current.CancellationToken);

        Assert.True(extraction.Succeeded);
        Assert.Equal(1.98328466981m, StockDividendNoticeParser.Parse(extraction.Text)?.AmountPerShare);
        Assert.Equal(new DateOnly(2026, 9, 3), StockDividendNoticeParser.Parse(extraction.Text)?.PaymentDate);
        Assert.True(OfficialCvmHtmlExtractor.IsOfficialCvmUrl(new Uri("https://www.rad.cvm.gov.br/ENETCONSULTA/frmDownloadDocumento.aspx?id=1")));
        Assert.False(OfficialCvmHtmlExtractor.IsOfficialCvmUrl(new Uri("https://example.com/ENETCONSULTA/frmDownloadDocumento.aspx?id=1")));
    }
}
