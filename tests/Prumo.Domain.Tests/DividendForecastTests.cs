using Microsoft.EntityFrameworkCore;
using Prumo.Api.Services;
using Prumo.Application.Abstractions;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Domain.Tests;

public sealed class DividendForecastTests
{
    [Fact]
    public async Task Official_shareholder_notice_is_not_replaced_by_a_newer_market_communication()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.BrazilianStock, 100);
        var date = CurrentMonthDate(20);
        AddDistribution(db, asset, $"Dividendos: R$ 1,00 por ação. Pagamento em {date:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-2));
        AddDistribution(db, asset, $"Dividendos: R$ 9,00 por ação. Pagamento em {date:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-1), "Comunicado ao Mercado");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db).GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Equal(100m, result!.EstimatedMonthlyAmount);
        Assert.Equal("Aviso aos Acionistas", Assert.Single(result.Items).Source);
    }

    [Theory]
    [InlineData("Fato Relevante")]
    [InlineData("Comunicado ao Mercado")]
    [InlineData(null)]
    public async Task Non_notice_stock_documents_are_not_used_even_with_explicit_values(string? category)
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.BrazilianStock, 100);
        var date = CurrentMonthDate(20);
        var text = $"Dividendos: R$ 1,00 por ação. JCP: R$ 0,50 por ação. Pagamento em {date:dd/MM/yyyy}.";
        AddDistribution(db, asset, text, DateTimeOffset.UtcNow.AddDays(-1), category);
        db.Documents.Local.Single().MarkDownloaded("memory://non-notice", "non-notice");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db, new MemoryStorage(text), new TextExtractor())
            .GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Equal(0m, result!.EstimatedMonthlyAmount);
        Assert.Null(Assert.Single(result.Items).PaymentDate);
    }

    [Fact]
    public async Task Shareholder_notice_without_a_file_or_event_uses_its_persisted_analysis()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.BrazilianStock, 100);
        var date = CurrentMonthDate(20);
        AddDistribution(db, asset, $"Dividendos: R$ 1,00 por ação. JCP: R$ 0,50 por ação. Pagamento em {date:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-1));
        db.InvestmentEvents.RemoveRange(db.InvestmentEvents.Local.ToList());
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db, new MemoryStorage(""), new TextExtractor())
            .GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Equal(150m, result!.EstimatedMonthlyAmount);
        Assert.Equal(2, Assert.Single(result.Items).Components!.Count);
    }

    [Fact]
    public async Task Future_stock_month_sums_received_announcements_but_excludes_future_publications()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.BrazilianStock, 100);
        var month = CurrentMonthDate(1).AddMonths(1);
        var date = month.AddDays(14);
        AddDistribution(db, asset, $"Dividendos: R$ 1,00 por ação. Pagamento em {date:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-2));
        AddDistribution(db, asset, $"JCP: R$ 0,50 por ação. Pagamento em {date:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-1));
        AddDistribution(db, asset, $"Dividendos: R$ 9,00 por ação. Pagamento em {date:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db, new MemoryStorage(""), new TextExtractor())
            .GetAsync(portfolio.Id, TestContext.Current.CancellationToken, month);

        Assert.Equal(150m, result!.EstimatedMonthlyAmount);
        Assert.Equal(date, Assert.Single(result.Items).PaymentDate);
    }

    [Fact]
    public async Task Duplicate_announcement_without_base_date_does_not_add_another_payment()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.BrazilianStock, 100);
        var date = CurrentMonthDate(20);
        AddDistribution(db, asset, $"JCP: R$ 0,50 por ação. Data-base: {date.AddDays(-10):dd/MM/yyyy}. Pagamento em {date:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-2));
        AddDistribution(db, asset, $"JCP: R$ 0,50 por ação. Pagamento em {date:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var result = await new DividendForecastService(db).GetAsync(portfolio.Id, TestContext.Current.CancellationToken);
        Assert.Equal(50m, result!.EstimatedMonthlyAmount);
        Assert.Single(Assert.Single(result.Items).Components!);
    }

    [Fact]
    public async Task Different_base_dates_preserve_separate_payments_on_the_same_day()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.BrazilianStock, 100);
        var date = CurrentMonthDate(20);
        AddDistribution(db, asset, $"JCP: R$ 0,50 por ação. Data-base: {date.AddDays(-10):dd/MM/yyyy}. Pagamento em {date:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-2));
        AddDistribution(db, asset, $"JCP: R$ 0,25 por ação. Data-base: {date.AddDays(-5):dd/MM/yyyy}. Pagamento em {date:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var result = await new DividendForecastService(db).GetAsync(portfolio.Id, TestContext.Current.CancellationToken);
        Assert.Equal(75m, result!.EstimatedMonthlyAmount);
        Assert.Equal(2, Assert.Single(result.Items).Components!.Count);
    }

    [Fact]
    public async Task Stock_dividends_and_net_jcp_from_one_document_are_summed_once()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.BrazilianStock, 100);
        var date = CurrentMonthDate(20);
        AddDistribution(db, asset, $"Dividendos: R$ 1,00 por ação. JCP: valor bruto por ação R$ 0,50. Valor líquido por ação R$ 0,425. Pagamento em {date:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-1));
        AddIncome(db, portfolio.Id, asset.Id, 142.50m, LocalNoon(CurrentMonthDate(1)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db).GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Equal(142.50m, result!.EstimatedMonthlyAmount);
        var item = Assert.Single(result.Items);
        Assert.Equal(date, item.PaymentDate);
        Assert.Equal(2, item.Components!.Count);
        Assert.Contains("JCP (líquido)", item.Source);
    }

    [Fact]
    public async Task Stock_payments_from_separate_documents_keep_dates_and_sum_the_month()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.BrazilianStock, 100);
        var dividendDate = CurrentMonthDate(10);
        var jcpDate = CurrentMonthDate(20);
        AddDistribution(db, asset, $"Dividendos: R$ 1,00 por ação. Pagamento em {dividendDate:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-2));
        AddDistribution(db, asset, $"JCP bruto: R$ 0,50 por ação. Pagamento em {jcpDate:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-1));
        AddDistribution(db, asset, $"JCP: R$ 9,00 por ação. Pagamento em {jcpDate.AddMonths(1):dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db).GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Equal(150m, result!.EstimatedMonthlyAmount);
        Assert.Equal(new[] { dividendDate, jcpDate }, result.Items.Select(x => x.PaymentDate!.Value));
        Assert.Contains("JCP (bruto)", result.Items[1].Source);
    }

    [Fact]
    public async Task Repeated_and_revised_stock_announcements_do_not_double_count()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.BrazilianStock, 100);
        var date = CurrentMonthDate(20);
        AddDistribution(db, asset, $"Dividendos: R$ 1,00 por ação. Pagamento em {date:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-3));
        AddDistribution(db, asset, $"Dividendos: R$ 1,00 por ação. Pagamento em {date:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-2));
        AddDistribution(db, asset, $"Dividendos: R$ 1,10 por ação. Pagamento em {date:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-1));
        AddDistribution(db, asset, $"JCP: R$ 0,50 por ação. Pagamento em {date:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-2));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db).GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Equal(160m, result!.EstimatedMonthlyAmount);
        Assert.Equal(2, Assert.Single(result.Items).Components!.Count);
    }

    [Fact]
    public async Task Original_document_replaces_its_analysis_without_losing_other_documents()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.BrazilianStock, 100);
        var date = CurrentMonthDate(20);
        AddDistribution(db, asset, $"Dividendos: R$ 2,00 por ação. Pagamento em {date:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-1));
        var document = db.Documents.Local.Single();
        document.MarkDownloaded("memory://stock", "stock-hash");
        AddDistribution(db, asset, $"JCP: R$ 0,50 por ação. Pagamento em {date:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-2));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db, new MemoryStorage($"Dividendos: R$ 1,00 por ação. Pagamento em {date:dd/MM/yyyy}."), new TextExtractor())
            .GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Equal(150m, result!.EstimatedMonthlyAmount);
        Assert.Equal(2, Assert.Single(result.Items).Components!.Count);
    }

    [Fact]
    public async Task Historical_income_uses_sao_paulo_daylight_saving_offset()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.BrazilianStock, 10);
        var month = new DateOnly(2018, 2, 1);
        AddIncome(db, portfolio.Id, asset.Id, 10, new DateTimeOffset(2018, 2, 1, 0, 0, 0, TimeSpan.FromHours(-2)));
        AddIncome(db, portfolio.Id, asset.Id, 900, new DateTimeOffset(2018, 3, 1, 0, 0, 0, TimeSpan.FromHours(-3)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db).GetAsync(portfolio.Id, TestContext.Current.CancellationToken, month);

        Assert.Equal(10m, result!.EstimatedMonthlyAmount);
        Assert.Equal(month, result.Items[0].PaymentDate);
    }

    [Fact]
    public async Task Requested_past_month_reads_old_documents_and_excludes_other_months()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.Fii, 100);
        var month = CurrentMonthDate(1).AddMonths(-12);
        AddAnalyzedFiiNotice(db, asset, 0.75m, month.AddDays(14), month.AddDays(-1), LocalNoon(month.AddDays(-1)));
        AddAnalyzedFiiNotice(db, asset, 1.25m, CurrentMonthDate(20), CurrentMonthDate(1), DateTimeOffset.UtcNow.AddDays(-1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db, new MemoryStorage(""), new TextExtractor())
            .GetAsync(portfolio.Id, TestContext.Current.CancellationToken, month);

        Assert.Equal(month, result!.ReferenceMonth);
        Assert.Equal(75m, result.EstimatedMonthlyAmount);
        Assert.Equal(month.AddDays(14), result.Items[0].PaymentDate);
        Assert.Equal("Aviso aos Cotistas", result.Items[0].Source);
    }

    [Fact]
    public async Task Requested_past_month_reads_old_analyzed_distribution()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.Fii, 100);
        var month = CurrentMonthDate(1).AddMonths(-12);
        AddDistribution(db, asset, $"R$ 0,80 por cota. Pagamento em {month.AddDays(10):dd/MM/yyyy}.", LocalNoon(month.AddDays(-1)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db).GetAsync(portfolio.Id, TestContext.Current.CancellationToken, month);

        Assert.Equal(80m, result!.EstimatedMonthlyAmount);
        Assert.Equal("Documento analisado", result.Items[0].Source);
        Assert.Equal(month.AddDays(10), result.Items[0].PaymentDate);
    }

    [Fact]
    public async Task Requested_future_month_uses_announced_payment_without_projecting_other_months()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.Fii, 100);
        var month = CurrentMonthDate(1).AddMonths(6);
        AddDistribution(db, asset, $"R$ 1,10 por cota. Pagamento em {month.AddDays(10):dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new DividendForecastService(db);

        var result = await service.GetAsync(portfolio.Id, TestContext.Current.CancellationToken, month);
        var empty = await service.GetAsync(portfolio.Id, TestContext.Current.CancellationToken, month.AddMonths(1));

        Assert.Equal(month, result!.ReferenceMonth);
        Assert.Equal(110m, result.EstimatedMonthlyAmount);
        Assert.Equal(0m, empty!.EstimatedMonthlyAmount);
        Assert.Null(empty.Items[0].PaymentDate);
    }

    [Fact]
    public async Task Requested_past_month_income_respects_local_month_boundaries_and_portfolio()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.BrazilianStock, 10);
        var month = CurrentMonthDate(1).AddMonths(-12);
        var start = new DateTimeOffset(month.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-3));
        var end = start.AddMonths(1);
        var other = new Portfolio("Outra");
        db.Portfolios.Add(other);
        AddIncome(db, portfolio.Id, asset.Id, 900, start.AddSeconds(-1));
        AddIncome(db, portfolio.Id, asset.Id, 10, start);
        AddIncome(db, portfolio.Id, asset.Id, 20, end.AddSeconds(-1));
        AddIncome(db, portfolio.Id, asset.Id, 900, end);
        AddIncome(db, other.Id, asset.Id, 900, start);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db).GetAsync(portfolio.Id, TestContext.Current.CancellationToken, month);

        Assert.Equal(30m, result!.EstimatedMonthlyAmount);
        Assert.Equal(month.AddMonths(1).AddDays(-1), result.Items[0].PaymentDate);
        Assert.Equal("Rendimento registrado", result.Items[0].Source);
    }

    [Fact]
    public async Task Explicit_distribution_uses_current_quantity_and_does_not_add_past_income_again()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.Fii, 100);
        AddIncome(db, portfolio.Id, asset.Id, 80, DateTimeOffset.UtcNow.AddDays(-35));
        AddIncome(db, portfolio.Id, asset.Id, 90, DateTimeOffset.UtcNow.AddDays(-5));
        AddDistribution(db, asset, $"Rendimento de R$ 0,95 por cota. Pagamento em {CurrentMonthDate(20):dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-2));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db).GetAsync(portfolio.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(95m, result.EstimatedMonthlyAmount);
        Assert.Single(result.Items);
        Assert.Equal("Documento analisado", result.Items[0].Source);
        Assert.Equal("https://source/distribution", result.Items[0].DocumentUrl);
    }

    [Fact]
    public async Task Only_current_month_income_from_the_requested_portfolio_is_included()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.BrazilianStock, 10);
        // The service excludes future income, so use a date that has already occurred even on the first day of the month.
        var paymentDate = CurrentMonthDate(1);
        var occurredAt = new DateTimeOffset(paymentDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-3));
        var other = new Portfolio("Outra");
        db.Portfolios.Add(other);
        AddIncome(db, portfolio.Id, asset.Id, 40, LocalNoon(CurrentMonthDate(10).AddMonths(-1)));
        AddIncome(db, other.Id, asset.Id, 900, occurredAt);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var first = await new DividendForecastService(db).GetAsync(portfolio.Id, CancellationToken.None);
        Assert.Single(first!.Items);
        Assert.Null(first.Items[0].EstimatedMonthlyAmount);

        AddIncome(db, portfolio.Id, asset.Id, 60, occurredAt);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var second = await new DividendForecastService(db).GetAsync(portfolio.Id, CancellationToken.None);
        Assert.Equal(60m, second!.EstimatedMonthlyAmount);
        Assert.Equal(paymentDate, second.Items[0].PaymentDate);
        Assert.Equal("Rendimento registrado", second.Items[0].Source);
    }

    [Fact]
    public async Task Distribution_without_explicit_per_unit_value_does_not_reuse_old_amount()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.Fii, 100);
        AddDistribution(db, asset, "R$ 0,90 por cota", DateTimeOffset.UtcNow.AddDays(-20));
        AddDistribution(db, asset, "Distribuição suspensa", DateTimeOffset.UtcNow.AddDays(-1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db).GetAsync(portfolio.Id, CancellationToken.None);

        Assert.Single(result!.Items);
        Assert.Null(result.Items[0].EstimatedMonthlyAmount);
        Assert.Equal(0, result.EstimatedMonthlyAmount);
    }

    [Fact]
    public async Task Ambiguous_document_values_are_not_projected()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.Fii, 100);
        AddDistribution(db, asset, "R$ 0,80 por cota antes e R$ 0,95 por cota agora", DateTimeOffset.UtcNow.AddDays(-1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db).GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Single(result!.Items);
        Assert.Null(result.Items[0].EstimatedMonthlyAmount);
    }

    [Fact]
    public async Task Includes_every_asset_and_only_sums_values_with_evidence()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.Fii, 100);
        var other = new Asset("OTHER3", "Sem histórico", AssetType.Stock, null);
        db.Assets.Add(other);
        db.PortfolioPositions.Add(new PortfolioPosition(portfolio.Id, other.Id, 10, 10, 10));
        AddDistribution(db, asset, $"R$ 1,20 por cota. Pagamento em {CurrentMonthDate(20):dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db).GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Equal(2, result!.Items.Count);
        Assert.Equal(120m, result.EstimatedMonthlyAmount);
        Assert.Null(result.Items.Single(x => x.AssetId == other.Id).EstimatedMonthlyAmount);
    }

    [Fact]
    public async Task Current_month_payment_dates_sort_before_assets_without_a_date()
    {
        await using var db = CreateDb();
        var (portfolio, later) = await Seed(db, AssetClass.Fii, 100);
        var earlier = new Asset("EARLY11", "Pagamento antes", AssetType.Fii, null);
        var sameDate = new Asset("TIE11", "Mesmo dia", AssetType.Fii, null);
        var unknown = new Asset("UNKNOWN3", "Sem data", AssetType.Stock, null);
        db.Assets.AddRange(earlier, sameDate, unknown);
        db.PortfolioPositions.AddRange(new PortfolioPosition(portfolio.Id, earlier.Id, 10, 10, 10), new PortfolioPosition(portfolio.Id, sameDate.Id, 10, 10, 10), new PortfolioPosition(portfolio.Id, unknown.Id, 10, 10, 10));
        var month = CurrentMonthDate(1);
        AddDistribution(db, later, $"R$ 1,00 por cota. Pagamento em {month.AddDays(14):dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-1));
        AddDistribution(db, earlier, $"R$ 2,00 por cota. Data de pagamento: {month.AddDays(7):dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-1));
        AddDistribution(db, sameDate, $"R$ 1,00 por cota. Pagamento em {month.AddDays(14):dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db).GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { earlier.Id, later.Id, sameDate.Id, unknown.Id }, result!.Items.Select(x => x.AssetId));
        Assert.Equal(month.AddDays(7), result.Items[0].PaymentDate);
        Assert.Equal(month.AddDays(14), result.Items[1].PaymentDate);
        Assert.Equal(month.AddDays(14), result.Items[2].PaymentDate);
        Assert.Null(result.Items[3].PaymentDate);
    }

    [Fact]
    public async Task Current_month_payment_date_is_kept_even_when_it_has_passed()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.Fii, 100);
        var payment = CurrentMonthDate(1);
        AddDistribution(db, asset, $"Data-base: {payment.AddDays(10):dd/MM/yyyy}. R$ 1,00 por cota. Pagamento em {payment:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-2));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db).GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Equal(payment, result!.Items[0].PaymentDate);
    }

    [Fact]
    public async Task Next_month_fii_payment_does_not_enter_the_current_month_total()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.Fii, 100);
        var nextMonth = CurrentMonthDate(20).AddMonths(1);
        AddDistribution(db, asset, $"R$ 1,00 por cota. Pagamento em {nextMonth:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db).GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Equal(CurrentMonthDate(1), result!.ReferenceMonth);
        Assert.Equal(0m, result.EstimatedMonthlyAmount);
        Assert.Null(result.Items[0].PaymentDate);
        Assert.Null(result.Items[0].EstimatedMonthlyAmount);
    }

    [Fact]
    public async Task Structured_fnet_notice_provides_amount_and_payment_date_without_ai_events()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.Fii, 100);
        var payment = CurrentMonthDate(20);
        var document = new Document(asset.Id, DocumentType.Other, DateTimeOffset.UtcNow.AddDays(-1), "FNET", "1320634",
            "https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id=1320634&cvm=true", Guid.NewGuid().ToString(),
            sourceCategory: "Aviso aos Cotistas - Estruturado", sourceType: "Rendimentos e Amortizações");
        document.MarkDownloaded("memory://notice", "test-hash");
        db.Documents.Add(document);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var text = $"Valor do provento (R$/unidade) | 0,09 |\nData do pagamento | {payment:dd/MM/yyyy} |";

        var result = await new DividendForecastService(db, new MemoryStorage(text), new TextExtractor())
            .GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Equal(9m, result!.EstimatedMonthlyAmount);
        Assert.Equal(payment, result.Items[0].PaymentDate);
        Assert.Equal("Aviso aos Cotistas", result.Items[0].Source);
        Assert.Equal(document.SourceUrl, result.Items[0].DocumentUrl);
    }

    [Fact]
    public async Task Fii_notice_uses_persisted_analysis_after_the_document_file_is_removed()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.Fii, 137);
        var payment = CurrentMonthDate(20);
        var document = new Document(asset.Id, DocumentType.Other, DateTimeOffset.UtcNow.AddDays(-1), "FNET", "notice-1",
            "https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id=1&cvm=true", Guid.NewGuid().ToString(),
            sourceCategory: "Aviso aos Cotistas - Estruturado", sourceType: "Rendimentos e Amortizações");
        var summary = $"O fundo anunciou rendimentos no valor de R$ 0,81 por cota, com pagamento programado para {payment:dd/MM/yyyy}.";
        var analysis = new DocumentAnalysis(Guid.NewGuid(), document.Id, "test", "test-v1", AnalysisImportance.Informative,
            AnalysisClassification.Distribution, false, SuggestedAction.NoAction, summary, "Impacto", "Impacto",
            new[] { asset.Ticker }, 1, "{}");
        db.Documents.Add(document);
        db.DocumentAnalyses.Add(analysis);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db, new MemoryStorage(""), new TextExtractor())
            .GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Equal(110.97m, result!.EstimatedMonthlyAmount);
        Assert.Equal(payment, result.Items[0].PaymentDate);
        Assert.Equal(document.SourceUrl, result.Items[0].DocumentUrl);
    }

    [Fact]
    public async Task Distinct_fii_notices_in_the_same_month_show_only_the_latest_notice()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.Fii, 100);
        AddAnalyzedFiiNotice(db, asset, 0.50m, CurrentMonthDate(10), CurrentMonthDate(1), DateTimeOffset.UtcNow.AddDays(-2));
        AddAnalyzedFiiNotice(db, asset, 0.75m, CurrentMonthDate(25), CurrentMonthDate(15), DateTimeOffset.UtcNow.AddDays(-1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db, new MemoryStorage(""), new TextExtractor())
            .GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Single(result!.Items);
        Assert.Equal(75m, result.EstimatedMonthlyAmount);
        Assert.Equal(CurrentMonthDate(25), result.Items[0].PaymentDate);
        Assert.Equal("Aviso aos Cotistas", result.Items[0].Source);
    }

    [Fact]
    public async Task Revised_fii_notice_replaces_the_earlier_notice_for_the_same_base_date()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.Fii, 100);
        AddAnalyzedFiiNotice(db, asset, 0.80m, CurrentMonthDate(21), CurrentMonthDate(15), DateTimeOffset.UtcNow.AddDays(-2));
        AddAnalyzedFiiNotice(db, asset, 1.00m, CurrentMonthDate(22), CurrentMonthDate(15), DateTimeOffset.UtcNow.AddDays(-1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db, new MemoryStorage(""), new TextExtractor())
            .GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Single(result!.Items);
        Assert.Equal(100m, result.EstimatedMonthlyAmount);
        Assert.Equal(CurrentMonthDate(22), result.Items[0].PaymentDate);
    }

    [Fact]
    public async Task Official_fii_notice_takes_priority_over_analyzed_event()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.Fii, 100);
        var payment = CurrentMonthDate(22);
        AddDistribution(db, asset, $"R$ 0,80 por cota. Pagamento em {payment:dd/MM/yyyy}.", DateTimeOffset.UtcNow.AddDays(-2));
        AddAnalyzedFiiNotice(db, asset, 1.00m, payment, CurrentMonthDate(15), DateTimeOffset.UtcNow.AddDays(-1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db, new MemoryStorage(""), new TextExtractor())
            .GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Single(result!.Items);
        Assert.Equal(100m, result.EstimatedMonthlyAmount);
        Assert.Equal("Aviso aos Cotistas", result.Items[0].Source);
    }

    [Fact]
    public async Task Stock_dividend_document_provides_current_month_payment_without_income_history()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.BrazilianStock, 25);
        var payment = CurrentMonthDate(20);
        var document = new Document(asset.Id, DocumentType.Communication, DateTimeOffset.UtcNow.AddDays(-1), "CVM", "1551317",
            "https://www.rad.cvm.gov.br/ENET/frmDownloadDocumento.aspx?numProtocolo=1551317", Guid.NewGuid().ToString(), sourceCategory: "Aviso aos Acionistas");
        document.MarkDownloaded("memory://stock", "stock-hash");
        db.Documents.Add(document);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var text = $"Dividendos intercalares. Valor por ação: R$ 1,98328466981. Data de pagamento: {payment:dd.MM.yyyy}.";

        var result = await new DividendForecastService(db, new MemoryStorage(text), new TextExtractor())
            .GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Equal(49.58m, result!.EstimatedMonthlyAmount);
        Assert.Equal(payment, result.Items[0].PaymentDate);
        Assert.Equal("Aviso aos Acionistas", result.Items[0].Source);
    }

    [Fact]
    public async Task Stock_dividend_event_is_used_when_stored_document_is_unavailable()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.BrazilianStock, 25);
        var payment = CurrentMonthDate(20);
        AddDistribution(db, asset, $"Dividendos de R$ 1,98328466981 por ação. Data de pagamento: {payment:dd.MM.yyyy}.", DateTimeOffset.UtcNow.AddDays(-1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db).GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Equal(49.58m, result!.EstimatedMonthlyAmount);
        Assert.Equal(payment, result.Items[0].PaymentDate);
    }

    [Fact]
    public async Task Previous_month_stock_dividend_is_excluded()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.BrazilianStock, 25);
        var paid = CurrentMonthDate(20).AddMonths(-1);
        AddDistribution(db, asset, $"Dividendos de R$ 1,98328466981 por ação. Data de pagamento: {paid:dd.MM.yyyy}.", DateTimeOffset.UtcNow.AddDays(-30));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DividendForecastService(db).GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Equal(0m, result!.EstimatedMonthlyAmount);
        Assert.Null(result.Items[0].PaymentDate);
        Assert.Null(result.Items[0].EstimatedMonthlyAmount);
        Assert.Equal("Sem pagamento informado neste mês", result.Items[0].Source);
    }

    private static DateOnly CurrentMonthDate(int day)
    {
        var localNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "America/Sao_Paulo");
        return new DateOnly(localNow.Year, localNow.Month, day);
    }

    private static DateTimeOffset LocalNoon(DateOnly day)
        => new(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(-3));

    private static PrumoDbContext CreateDb() => new(new DbContextOptionsBuilder<PrumoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(Portfolio, Asset)> Seed(PrumoDbContext db, AssetClass assetClass, decimal quantity)
    {
        var portfolio = new Portfolio("Principal");
        var asset = new Asset("TEST11", "Teste", assetClass == AssetClass.Fii ? AssetType.Fii : AssetType.Stock, null, assetClass);
        db.Portfolios.Add(portfolio);
        db.Assets.Add(asset);
        db.PortfolioPositions.Add(new PortfolioPosition(portfolio.Id, asset.Id, quantity, 10, 10));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (portfolio, asset);
    }

    private static void AddIncome(PrumoDbContext db, Guid portfolioId, Guid assetId, decimal amount, DateTimeOffset date)
        => db.PortfolioCashFlows.Add(new PortfolioCashFlow(portfolioId, PortfolioCashFlowType.Income, amount, date, FinancialSource.Manual, assetId));

    private static void AddAnalyzedFiiNotice(PrumoDbContext db, Asset asset, decimal amount, DateOnly payment, DateOnly baseDate, DateTimeOffset publishedAt)
    {
        var id = Guid.NewGuid().ToString();
        var document = new Document(asset.Id, DocumentType.Other, publishedAt, "FNET", id,
            $"https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id={id}&cvm=true", Guid.NewGuid().ToString(),
            sourceCategory: "Aviso aos Cotistas - Estruturado", sourceType: "Rendimentos e Amortizações");
        var summary = $"Rendimentos no valor de R$ {amount:0.00} por cota, com data-base em {baseDate:dd/MM/yyyy} e pagamento em {payment:dd/MM/yyyy}.";
        db.Documents.Add(document);
        db.DocumentAnalyses.Add(new DocumentAnalysis(Guid.NewGuid(), document.Id, "test", "test-v1", AnalysisImportance.Informative,
            AnalysisClassification.Distribution, false, SuggestedAction.NoAction, summary, "Impacto", "Impacto", new[] { asset.Ticker }, 1, "{}"));
    }

    private static void AddDistribution(PrumoDbContext db, Asset asset, string summary, DateTimeOffset publishedAt, string? stockCategory = "Aviso aos Acionistas")
    {
        var document = new Document(asset.Id, DocumentType.RelevantFact, publishedAt, "CVM", Guid.NewGuid().ToString(), "https://source/distribution", Guid.NewGuid().ToString(),
            sourceCategory: asset.AssetClass == AssetClass.BrazilianStock ? stockCategory : null);
        var analysis = new DocumentAnalysis(Guid.NewGuid(), document.Id, "test", "test-v1", AnalysisImportance.Informative, AnalysisClassification.Distribution, false, SuggestedAction.NoAction, summary, "Impacto", "Impacto", new[] { asset.Ticker }, 1, "{}");
        var item = new InvestmentEvent(analysis.Id, InvestmentEventType.Distribution, publishedAt, summary, new[] { summary });
        item.AffectedAssets.Add(asset);
        db.Documents.Add(document);
        db.DocumentAnalyses.Add(analysis);
        db.InvestmentEvents.Add(item);
    }

    private sealed class MemoryStorage(string content) : IFileStorage
    {
        public Task<StoredFile> SaveAsync(Guid documentId, Stream data, string extension, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)));
    }

    private sealed class TextExtractor : IPdfTextExtractor
    {
        public async Task<PdfTextExtractionResult> ExtractAsync(Stream pdf, CancellationToken cancellationToken = default)
        {
            using var reader = new StreamReader(pdf);
            return new PdfTextExtractionResult(true, await reader.ReadToEndAsync(cancellationToken), null);
        }
    }
}