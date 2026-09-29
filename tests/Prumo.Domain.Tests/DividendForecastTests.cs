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
        var other = new Portfolio("Outra");
        db.Portfolios.Add(other);
        AddIncome(db, portfolio.Id, asset.Id, 40, LocalNoon(CurrentMonthDate(10).AddMonths(-1)));
        AddIncome(db, other.Id, asset.Id, 900, LocalNoon(CurrentMonthDate(10)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var first = await new DividendForecastService(db).GetAsync(portfolio.Id, CancellationToken.None);
        Assert.Single(first!.Items);
        Assert.Null(first.Items[0].EstimatedMonthlyAmount);

        AddIncome(db, portfolio.Id, asset.Id, 60, LocalNoon(CurrentMonthDate(10)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var second = await new DividendForecastService(db).GetAsync(portfolio.Id, CancellationToken.None);
        Assert.Equal(60m, second!.EstimatedMonthlyAmount);
        Assert.Equal(CurrentMonthDate(10), second.Items[0].PaymentDate);
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
    public async Task Stock_dividend_document_provides_current_month_payment_without_income_history()
    {
        await using var db = CreateDb();
        var (portfolio, asset) = await Seed(db, AssetClass.BrazilianStock, 25);
        var payment = CurrentMonthDate(20);
        var document = new Document(asset.Id, DocumentType.Communication, DateTimeOffset.UtcNow.AddDays(-1), "CVM", "1551317",
            "https://www.rad.cvm.gov.br/ENET/frmDownloadDocumento.aspx?numProtocolo=1551317", Guid.NewGuid().ToString());
        document.MarkDownloaded("memory://stock", "stock-hash");
        db.Documents.Add(document);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var text = $"Dividendos intercalares. Valor por ação: R$ 1,98328466981. Data de pagamento: {payment:dd.MM.yyyy}.";

        var result = await new DividendForecastService(db, new MemoryStorage(text), new TextExtractor())
            .GetAsync(portfolio.Id, TestContext.Current.CancellationToken);

        Assert.Equal(49.58m, result!.EstimatedMonthlyAmount);
        Assert.Equal(payment, result.Items[0].PaymentDate);
        Assert.Equal("Comunicado de dividendos", result.Items[0].Source);
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

    private static void AddDistribution(PrumoDbContext db, Asset asset, string summary, DateTimeOffset publishedAt)
    {
        var document = new Document(asset.Id, DocumentType.RelevantFact, publishedAt, "CVM", Guid.NewGuid().ToString(), "https://source/distribution", Guid.NewGuid().ToString());
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
