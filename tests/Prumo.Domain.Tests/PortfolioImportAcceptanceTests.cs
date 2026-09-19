using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prumo.Api.Controllers;
using Prumo.Application.Abstractions;
using Prumo.Application.PortfolioImports;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.PortfolioImports;
using Prumo.Infrastructure.Reconciliation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Prumo.Api.Services;
using Prumo.Application.Intelligence;
using Prumo.Application.Portfolio;
using Prumo.Infrastructure.Ai;
using Prumo.Worker;
using System.Text.Json;

namespace Prumo.Domain.Tests;

public sealed class PortfolioImportAcceptanceTests
{
    [RelationalFact]
    public async Task Imported_portfolio_generates_published_guidance_and_preserves_partial_executions()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal("Completed", (await fixture.ImportAsync()).Status);
        var db = fixture.Db;
        var portfolioId = fixture.PortfolioId;
        var stock = await db.Assets.SingleAsync();
        var fund = new Asset("TEST11", "Test fund", AssetType.Fii, "Logística", AssetClass.Fii);
        db.AddRange(fund, new PortfolioPosition(portfolioId, fund.Id, 5, 30, 32),
            new TargetAllocation(portfolioId, AssetClass.BrazilianStock, 50), new TargetAllocation(portfolioId, AssetClass.Fii, 50));
        await db.SaveChangesAsync();
        // Start a fresh request identity map after the import/position updates.
        db.ChangeTracker.Clear();
        var current = new PortfolioAssessmentCurrentService(db, new PortfolioImpactAssessmentService(),
            new PortfolioAssessmentFreshnessEvaluator(), NullLogger<PortfolioAssessmentCurrentService>.Instance);
        var controller = new ContributionGuidanceController(db, current);
        Assert.IsType<AcceptedResult>((await controller.Generate(portfolioId, new(1000), default)).Result);
        var guidanceId = await db.ContributionGuidances.Select(x => x.Id).SingleAsync();

        var services = new ServiceCollection();
        services.AddDbContext<PrumoDbContext>(options => options.UseNpgsql(db.Database.GetConnectionString()));
        services.AddSingleton(Options.Create(new AiProviderOptions { Provider = "Fake", Contribution = new() { Provider = "Deterministic" } }));
        services.AddSingleton<WorkerMetrics>();
        await using var provider = services.BuildServiceProvider();
        using var worker = new ContributionGuidanceWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ContributionGuidanceWorker>.Instance, provider.GetRequiredService<WorkerMetrics>());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await worker.StartAsync(timeout.Token);
        try
        {
            while (true)
            {
                var job = await db.ContributionGuidanceJobs.AsNoTracking().SingleAsync(timeout.Token);
                Assert.NotEqual(ContributionGuidanceJobStatus.Failed, job.Status);
                if (job.Status == ContributionGuidanceJobStatus.Completed) break;
                await Task.Delay(100, timeout.Token);
            }
        }
        finally { await worker.StopAsync(CancellationToken.None); }

        db.ChangeTracker.Clear();
        var response = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(
            (await controller.Get(portfolioId, guidanceId, default)).Result).Value);
        Assert.Equal(2, response.GetProperty("allocations").GetArrayLength());
        Assert.True(response.GetProperty("analysis").GetProperty("usedFallback").GetBoolean());
        Assert.Equal("deterministic-configured", response.GetProperty("analysis").GetProperty("executionPath").GetString());

        var executions = new PortfolioTransactionsController(db);
        Assert.IsType<OkObjectResult>((await executions.Confirm(portfolioId, new(guidanceId, [new(stock.Id, 1, 32)]), default)).Result);
        Assert.IsType<ConflictObjectResult>((await executions.Confirm(portfolioId, new(guidanceId, [new(stock.Id, 1, 32)]), default)).Result);
        Assert.IsType<OkObjectResult>((await executions.Confirm(portfolioId, new(guidanceId, [new(fund.Id, 2, 31)]), default)).Result);
        db.ChangeTracker.Clear();
        Assert.Equal(11, (await db.PortfolioPositions.SingleAsync(x => x.AssetId == stock.Id)).Quantity);
        Assert.Equal(7, (await db.PortfolioPositions.SingleAsync(x => x.AssetId == fund.Id)).Quantity);
        Assert.Equal(94, await db.PortfolioTransactions.Where(x => x.ContributionGuidanceId == guidanceId).SumAsync(x => x.TotalAmount));
        Assert.Equal(2, await db.PortfolioTransactions.CountAsync(x => x.ContributionGuidanceId == guidanceId));
    }

    [RelationalFact]
    public async Task Repeated_b3_import_is_idempotent_and_creates_no_duplicate_state()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.ImportAsync();
        var second = await fixture.ImportAsync();
        Assert.Equal("Completed", first.Status);
        Assert.Equal("AlreadyImported", second.Status);
        Assert.Equal(1, await fixture.Db.Assets.CountAsync());
        Assert.Equal(1, await fixture.Db.PortfolioPositions.CountAsync());
        Assert.Equal(1, await fixture.Db.PortfolioTransactions.CountAsync());
        Assert.Equal(1, await fixture.Db.PortfolioImportSessions.CountAsync());
        Assert.Empty(await fixture.Db.TransactionReconciliations.ToListAsync());
    }

    [RelationalFact]
    public async Task Provider_failure_after_import_does_not_rollback_canonical_portfolio_state()
    {
        await using var fixture = await Fixture.CreateAsync(new ThrowingDocumentSource());
        var result = await fixture.ImportAsync();
        Assert.Equal("Completed", result.Status);
        Assert.Equal(1, await fixture.Db.Assets.CountAsync());
        Assert.Equal(1, await fixture.Db.PortfolioPositions.CountAsync());
        Assert.Equal(1, await fixture.Db.PortfolioTransactions.CountAsync());
        var retry = await fixture.ImportAsync();
        Assert.Equal("AlreadyImported", retry.Status);
        Assert.Equal(1, await fixture.Db.PortfolioTransactions.CountAsync());
    }


    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string adminConnection;
        private readonly string database;
        private readonly IFormFile file;
        public PrumoDbContext Db { get; }
        public Guid PortfolioId => portfolio.Id;
        private readonly Portfolio portfolio;
        private readonly IDocumentSource source;

        private Fixture(string adminConnection, string database, PrumoDbContext db, Portfolio portfolio, IFormFile file, IDocumentSource source)
        { this.adminConnection = adminConnection; this.database = database; Db = db; this.portfolio = portfolio; this.file = file; this.source = source; }

        public static async Task<Fixture> CreateAsync(IDocumentSource? source = null)
        {
            var admin = new NpgsqlConnectionStringBuilder("Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=dev123");
            var database = $"prumo_import_{Guid.NewGuid():N}";
            await using var connection = new NpgsqlConnection(admin.ConnectionString); await connection.OpenAsync();
            await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection)) await command.ExecuteNonQueryAsync();
            var cs = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = database }.ConnectionString;
            var db = new PrumoDbContext(new DbContextOptionsBuilder<PrumoDbContext>().UseNpgsql(cs).Options);
            await db.Database.MigrateAsync();
            var portfolio = new Portfolio("import"); db.Portfolios.Add(portfolio); await db.SaveChangesAsync();
            var bytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x01 };
            var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "portfolio.xlsx") { Headers = new HeaderDictionary(), ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" };
            return new Fixture(admin.ConnectionString, database, db, portfolio, file, source ?? new EmptyDocumentSource());
        }

        public async Task<ImportConfirmationDto> ImportAsync()
        {
            var controller = new PortfolioImportsController(Db, new[] { new FakeImporter() }, new NewAssetBootstrapService(Db, source, NullLogger<NewAssetBootstrapService>.Instance), new TransactionReconciliationService(Db), NullLogger<PortfolioImportsController>.Instance, new DevelopmentEnvironment());
            var preview = await controller.Preview(portfolio.Id, file, default);
            var previewBody = Assert.IsType<ImportPreviewDto>(Assert.IsAssignableFrom<Microsoft.AspNetCore.Mvc.ObjectResult>(preview.Result).Value);
            var result = await controller.Confirm(portfolio.Id, previewBody.ImportSessionId, default);
            return Assert.IsType<ImportConfirmationDto>(Assert.IsAssignableFrom<Microsoft.AspNetCore.Mvc.ObjectResult>(result.Result).Value);
        }

        public async ValueTask DisposeAsync()
        { await Db.DisposeAsync(); await using var connection = new NpgsqlConnection(adminConnection); await connection.OpenAsync(); await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection); await command.ExecuteNonQueryAsync(); }
    }

    private sealed class FakeImporter : IPortfolioImporter
    {
        public bool CanHandle(string fileName, string? contentType, ReadOnlySpan<byte> header) => true;
        public Task<NormalizedPortfolioImport> ParseAsync(Stream content, CancellationToken cancellationToken = default)
        {
            var date = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
            var import = new NormalizedPortfolioImport(PortfolioImportSource.B3File, date, SnapshotCompleteness.Complete, [new("PETR4", "Petrobras", AssetType.Stock, AssetClass.BrazilianStock, 10, 32, 320, 30, "B3")], [], []);
            import = import with { Transactions = [new("PETR4", date, 10, 32, 320, "B3", PortfolioTransactionType.Buy, FinancialDatePrecision.Exact)] };
            return Task.FromResult(import);
        }
    }

    private class EmptyDocumentSource : IDocumentSource
    { public virtual string Name => "Test"; public virtual Task<IReadOnlyList<DocumentCandidate>> FindAsync(IReadOnlyCollection<string> tickers, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DocumentCandidate>>([]); }
    private sealed class ThrowingDocumentSource : EmptyDocumentSource
    { public override string Name => "Throwing"; public override Task<IReadOnlyList<DocumentCandidate>> FindAsync(IReadOnlyCollection<string> tickers, CancellationToken cancellationToken = default) => throw new InvalidOperationException("provider failure"); }
    private sealed class DevelopmentEnvironment : IWebHostEnvironment
    { public string ApplicationName { get; set; } = "Tests"; public string EnvironmentName { get; set; } = "Development"; public string WebRootPath { get; set; } = "."; public string ContentRootPath { get; set; } = "."; public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider(); public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider(); }
}
