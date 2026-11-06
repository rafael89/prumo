using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Prumo.Api.Controllers;
using Prumo.Api.Services;
using Prumo.Application.Intelligence;
using Prumo.Application.Portfolio;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Historical;
using Prumo.Infrastructure.Performance;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Reconciliation;

namespace Prumo.Domain.Tests;

public sealed class PerformanceRelationalTests
{
    [RelationalFact]
    public async Task Historical_query_count_is_independent_of_series_length_and_paged_reads_run_in_postgresql()
    {
        var admin = new NpgsqlConnectionStringBuilder("Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=dev123");
        var database = $"prumo_performance_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection)) await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        try
        {
            var counter = new ReadCounter();
            var options = new DbContextOptionsBuilder<PrumoDbContext>().UseNpgsql(new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = database }.ConnectionString).AddInterceptors(counter).Options;
            await using var db = new PrumoDbContext(options);
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
            var portfolio = new Portfolio("Performance");
            var asset = new Asset("PERF3", "Performance", AssetType.Stock, null);
            portfolio.Positions.Add(new(portfolio.Id, asset.Id, 10, 10, 10));
            var anchor = new PortfolioHistoricalAnchor(portfolio.Id, new(2025, 1, 1), FinancialSource.Manual, FinancialDatePrecision.Exact);
            anchor.Items.Add(new(anchor.Id, asset.Id, 10, 10));
            db.AddRange(portfolio, asset, anchor, new PortfolioCashAnchor(portfolio.Id, new(2025, 1, 1), 1000, FinancialSource.Manual, FinancialDatePrecision.Exact));
            for (var day = 1; day <= 30; day++)
            {
                db.AssetPrices.Add(new(asset.Id, new(2025, 1, day), 10 + day, "BRL", "test", DateTimeOffset.UtcNow));
                var document = new Document(asset.Id, DocumentType.RelevantFact, new(2025, 1, day, 0, 0, 0, TimeSpan.Zero), "Test", null, "https://example.test/document", Guid.NewGuid().ToString());
                var job = new ProcessingJob(document.Id, ProcessingJobType.AnalyzeDocument, DateTimeOffset.UtcNow);
                var analysis = new DocumentAnalysis(job.Id, document.Id, "test", "test", AnalysisImportance.Relevant, AnalysisClassification.Other, true, SuggestedAction.Monitor, "Summary", "Impact", "Portfolio impact", [asset.Ticker], 1, "{}");
                var evt = new InvestmentEvent(analysis.Id, InvestmentEventType.Other, document.PublishedAt, "Event", []);
                evt.AffectedAssets.Add(asset);
                evt.Evidence.Add(new(evt.Id, analysis.Id, document.Id, "Evidence"));
                var assessment = new PortfolioAssessment(portfolio.Id, evt.Id, AttentionPriority.High, ActionRequirement.Review, CapitalDecisionImpact.None, AssessmentStatus.Active, AssessmentUserState.Unread, "Summary", "Reason", "Monitor", IntelligenceConfidence.High, null, PortfolioImpactAssessmentService.CurrentVersion, PortfolioSnapshotKeyCalculator.Calculate(portfolio), evt.Version);
                db.AddRange(document, job, analysis, evt, assessment);
            }
            db.Add(new AssetFundamentalSnapshot(asset.Id, new(2025, 1, 1), FinancialPeriodType.Annual, revenue: 10));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            db.ChangeTracker.Clear();
            var coverage = new CoverageService(db);
            var reconciliation = new TransactionReconciliationService(db);
            var state = new HistoricalFinancialStateService(db, coverage, reconciliation, new HistoricalCashStateService(db, coverage, reconciliation));
            var performance = new HistoricalPerformanceService(db, state, coverage);
            counter.Reads = 0;
            var shortSeries = await performance.Get(portfolio.Id, new(2025, 1, 1), new(2025, 1, 2), TestContext.Current.CancellationToken);
            var shortReads = counter.Reads;
            counter.Reads = 0;
            var longSeries = await performance.Get(portfolio.Id, new(2025, 1, 1), new(2025, 1, 30), TestContext.Current.CancellationToken);
            Assert.Equal(2, shortSeries!.Series.Count);
            Assert.Equal(30, longSeries!.Series.Count);
            Assert.Equal(shortReads, counter.Reads);
            Assert.InRange(counter.Reads, 1, 20);

            var radarResult = await new RadarController(db).Get(portfolio.Id, null, null, null, page: 2, pageSize: 10, ct: TestContext.Current.CancellationToken);
            var radar = Assert.IsType<RadarResponse>(Assert.IsType<OkObjectResult>(radarResult.Result).Value);
            Assert.Equal(30, radar.TotalItems);
            Assert.Equal(10, radar.AttentionNow.Count);
            Assert.NotNull((await new RadarController(db).GetNotificationVersion(portfolio.Id, TestContext.Current.CancellationToken)).Result);
            var dashboard = Assert.IsType<DashboardResponse>(Assert.IsType<OkObjectResult>((await new PortfoliosController(db).GetDashboard(portfolio.Id, cancellationToken: TestContext.Current.CancellationToken)).Result).Value);
            Assert.Equal(5, dashboard.RecentUpdates!.Count);
            Assert.Equal(30, dashboard.RecentUpdatesTotalItems);
            Assert.Single((await db.Portfolios.WithContributionFinancials(db).SingleAsync(TestContext.Current.CancellationToken)).Positions.Single().Asset!.FundamentalSnapshots);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class ReadCounter : DbCommandInterceptor
    {
        public int Reads;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult(result);
        }
    }
}
