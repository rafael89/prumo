using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prumo.Api.Controllers;
using Prumo.Api.Models;
using Prumo.Api.Services;
using Prumo.Application.Contributions;
using Prumo.Application.Intelligence;
using Prumo.Application.Portfolio;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Reconciliation;
using Prumo.Infrastructure.Historical;
using Prumo.Infrastructure.Performance;
using Prumo.Infrastructure.Logging;

namespace Prumo.Domain.Tests;

public sealed class PerformanceOptimizationTests
{
    [Fact]
    public async Task Series_includes_assets_held_only_between_the_endpoint_anchors()
    {
        await using var db = CreateDb();
        var portfolio = new Portfolio("Intermediate holding");
        var first = new Asset("FIRST3", "First", AssetType.Stock, null);
        var middle = new Asset("MID3", "Middle", AssetType.Stock, null);
        db.AddRange(portfolio, first, middle);
        foreach (var (day, asset) in new[] { (1, first), (3, middle), (5, first) })
        {
            var anchor = new PortfolioHistoricalAnchor(portfolio.Id, new(2025, 1, day), FinancialSource.Manual, FinancialDatePrecision.Exact);
            anchor.Items.Add(new(anchor.Id, asset.Id, 10, 10));
            db.Add(anchor);
        }
        db.AssetPrices.AddRange(new(first.Id, new(2025, 1, 1), 10, "BRL", "test", DateTimeOffset.UtcNow),
            new AssetPrice(middle.Id, new(2025, 1, 4), 20, "BRL", "test", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var coverage = new CoverageService(db);
        var state = new HistoricalFinancialStateService(db, coverage, new TransactionReconciliationService(db));

        var result = await new HistoricalPerformanceService(db, state, coverage).Get(portfolio.Id, new(2025, 1, 1), new(2025, 1, 5), TestContext.Current.CancellationToken);

        Assert.Equal(100, result!.StartingValuation.Value);
        Assert.Equal(100, result.EndingValuation.Value);
        Assert.Equal(200, result.Series.Single(x => x.Date == new DateOnly(2025, 1, 4)).InvestedValue);
    }

    [Fact]
    public async Task Buffered_logging_drains_concurrent_producers_before_disposal_returns()
    {
        var directory = Path.Combine(Path.GetTempPath(), "prumo-buffered-logs", Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BufferedFileLogWriter(directory, "test"))
                await Task.WhenAll(Enumerable.Range(0, 512).Select(i => Task.Run(() => writer.Write(DateTimeOffset.Now, $"entry-{i}\n"), TestContext.Current.CancellationToken)));
            var lines = File.ReadAllLines(Assert.Single(Directory.GetFiles(directory, "test-*.txt")));
            Assert.Equal(512, lines.Length);
            Assert.Equal(512, lines.Distinct().Count());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Current_financial_queries_keep_sparse_metrics_and_same_period_conflicts()
    {
        await using var db = CreateDb();
        var asset = new Asset("TEST3", "Test", AssetType.Stock, null);
        var old = new AssetFundamentalSnapshot(asset.Id, new(2024, 12, 31), FinancialPeriodType.Annual, revenue: 100, netIncome: 10);
        var recent = new AssetFundamentalSnapshot(asset.Id, new(2025, 12, 31), FinancialPeriodType.Annual, FinancialDataSource.Manual, netIncome: 20);
        var conflicting = new AssetFundamentalSnapshot(asset.Id, new(2025, 12, 31), FinancialPeriodType.Annual, FinancialDataSource.Provider, netIncome: 30);
        var obsolete = new AssetFundamentalSnapshot(asset.Id, new(2023, 12, 31), FinancialPeriodType.Annual, netIncome: 1);
        db.AddRange(asset, old, recent, conflicting, obsolete);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var fullEvidence = MetricEvidenceResolver.Resolve(await db.Assets.AsNoTracking().Include(x => x.FundamentalSnapshots).SingleAsync(TestContext.Current.CancellationToken));

        var loaded = await db.Assets.AsNoTracking().WithContributionFinancials(db).SingleAsync(TestContext.Current.CancellationToken);
        var evidence = MetricEvidenceResolver.Resolve(loaded);

        Assert.Equal(3, loaded.FundamentalSnapshots.Count);
        Assert.DoesNotContain(loaded.FundamentalSnapshots, x => x.Id == obsolete.Id);
        Assert.Equal(100, evidence.Metrics.Single(x => x.Metric == "Revenue").Canonical!.Value);
        Assert.Equal(MetricEvidenceResolutionStatus.Conflict, evidence.Metrics.Single(x => x.Metric == "NetIncome").Status);
        Assert.Equal(fullEvidence.Metrics.Select(x => (x.Metric, x.Status, x.Canonical)), evidence.Metrics.Select(x => (x.Metric, x.Status, x.Canonical)));
    }

    [Fact]
    public void Filtered_financial_includes_translate_to_postgresql()
    {
        using var db = new PrumoDbContext(new DbContextOptionsBuilder<PrumoDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=unused;Password=unused").Options);
        // Single query forces translation of every filtered include without opening a connection.
        Assert.Contains("NOT EXISTS", db.Assets.AsNoTracking().WithContributionFinancials(db).AsSingleQuery().ToQueryString());
        Assert.Contains("NOT EXISTS", db.Portfolios.WithContributionFinancials(db).AsSingleQuery().ToQueryString());
    }

    [Fact]
    public async Task News_is_bounded_and_pages_cover_the_entire_feed_without_duplicates()
    {
        await using var db = CreateDb();
        var portfolio = new Portfolio("News");
        var asset = new Asset("NEWS3", "News", AssetType.Stock, null);
        portfolio.Positions.Add(new(portfolio.Id, asset.Id, 10, 10, 10));
        db.AddRange(portfolio, asset);
        var eventDetails = new RadarEventDetails(Importance: nameof(AnalysisImportance.Critical));
        for (var i = 0; i < 13; i++) AddAnalysis(db, portfolio, asset, new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i), eventDetails);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var controller = new PortfoliosController(db);
        var dashboard = Assert.IsType<DashboardResponse>(Assert.IsType<OkObjectResult>((await controller.GetDashboard(portfolio.Id, cancellationToken: TestContext.Current.CancellationToken)).Result).Value);
        Assert.Equal(13, dashboard.RecentUpdatesTotalItems);
        Assert.Equal(5, dashboard.RecentUpdates!.Count);
        var items = new List<DashboardAssessment>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await controller.GetNews(portfolio.Id, page, 5, TestContext.Current.CancellationToken);
            var response = Assert.IsType<PaginatedResponse<DashboardAssessment>>(Assert.IsType<OkObjectResult>(result.Result).Value);
            Assert.Equal(3, response.TotalPages);
            items.AddRange(response.Items);
        }
        Assert.Equal(13, items.Count);
        Assert.Equal(13, items.Select(x => x.DocumentId).Distinct().Count());
        Assert.Equal(dashboard.RecentUpdates.Select(x => x.DocumentId), items.Take(5).Select(x => x.DocumentId));
        Assert.All(dashboard.RecentUpdates.Concat(items), item =>
        {
            Assert.True(item.HasRadarAssessment);
            Assert.Equal(nameof(AnalysisImportance.Relevant), item.DocumentImportance);
        });
    }

    [Fact]
    public async Task Notification_version_is_stable_until_a_dependency_changes()
    {
        await using var db = CreateDb();
        var portfolio = new Portfolio("Notifications");
        var asset = new Asset("NOTE3", "Notifications", AssetType.Stock, null);
        portfolio.Positions.Add(new(portfolio.Id, asset.Id, 10, 10, 10));
        db.AddRange(portfolio, asset);
        var assessment = AddAnalysis(db, portfolio, asset, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var controller = new RadarController(db);
        async Task<string> Version() => JsonSerializer.Serialize(Assert.IsType<OkObjectResult>((await controller.GetNotificationVersion(portfolio.Id, TestContext.Current.CancellationToken)).Result).Value);
        var initial = await Version();
        Assert.Equal(initial, await Version());
        assessment.MarkSeen();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(initial, await Version());
        var seen = await Version();
        db.TargetAllocations.Add(new(portfolio.Id, AssetClass.BrazilianStock, 100));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(seen, await Version());
    }

    [Fact]
    public async Task Reconciliation_keeps_window_boundaries_and_original_pair_orientation()
    {
        await using var db = CreateDb();
        var portfolio = new Portfolio("Reconciliation");
        var asset = new Asset("PAIR3", "Pair", AssetType.Stock, null);
        db.AddRange(portfolio, asset);
        PortfolioTransaction Add(int day, FinancialSource source, FinancialDatePrecision precision)
        {
            var transaction = new PortfolioTransaction(portfolio.Id, PortfolioTransactionType.Buy,
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(day), null, 100, source: source, datePrecision: precision);
            transaction.Items.Add(new(transaction.Id, asset.Id, 10, 10));
            db.Add(transaction);
            return transaction;
        }
        var late = Add(31, FinancialSource.Manual, FinancialDatePrecision.Exact);
        var approximate = Add(0, FinancialSource.B3Import, FinancialDatePrecision.PeriodStart);
        var outside = Add(32, FinancialSource.Manual, FinancialDatePrecision.Exact);
        var exact = Add(0, FinancialSource.Manual, FinancialDatePrecision.Exact);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var candidates = await new TransactionReconciliationService(db).Candidates(portfolio.Id, TestContext.Current.CancellationToken);
        Assert.Contains(candidates, x => x.TransactionAId == late.Id && x.TransactionBId == approximate.Id);
        Assert.Contains(candidates, x => x.TransactionAId == approximate.Id && x.TransactionBId == exact.Id);
        Assert.DoesNotContain(candidates, x => x.TransactionAId == outside.Id || x.TransactionBId == outside.Id);
    }

    private static PortfolioAssessment AddAnalysis(PrumoDbContext db, Portfolio portfolio, Asset asset, DateTimeOffset publishedAt, RadarEventDetails? eventDetails = null)
    {
        var document = new Document(asset.Id, DocumentType.RelevantFact, publishedAt, "Test", null, "https://example.test/doc", Guid.NewGuid().ToString());
        var analysis = new DocumentAnalysis(Guid.NewGuid(), document.Id, "test", "test", AnalysisImportance.Relevant, AnalysisClassification.Other, true, SuggestedAction.Monitor, "Summary", "Impact", "Portfolio impact", [asset.Ticker], 1, "{}");
        var investmentEvent = new InvestmentEvent(analysis.Id, InvestmentEventType.Other, publishedAt, "Event", []);
        if (eventDetails is not null) investmentEvent.SetRadarDetails(eventDetails);
        investmentEvent.AffectedAssets.Add(asset);
        investmentEvent.Evidence.Add(new(investmentEvent.Id, analysis.Id, document.Id, "Evidence"));
        var assessment = new PortfolioAssessment(portfolio.Id, investmentEvent.Id, AttentionPriority.High, ActionRequirement.Review, CapitalDecisionImpact.None, AssessmentStatus.Active, AssessmentUserState.Unread, "Summary", "Reason", "Monitor", IntelligenceConfidence.High, null, PortfolioImpactAssessmentService.CurrentVersion, PortfolioSnapshotKeyCalculator.Calculate(portfolio), investmentEvent.Version);
        db.AddRange(document, analysis, investmentEvent, assessment);
        return assessment;
    }

    private static PrumoDbContext CreateDb() => new(new DbContextOptionsBuilder<PrumoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
