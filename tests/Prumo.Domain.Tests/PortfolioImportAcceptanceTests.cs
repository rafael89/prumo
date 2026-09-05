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

namespace Prumo.Domain.Tests;

public sealed class PortfolioImportAcceptanceTests
{
    [Fact]
    public async Task Repeated_b3_import_is_idempotent_and_creates_no_duplicate_state()
    {
        if (!RelationalEnabled()) return;
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

    [Fact]
    public async Task Provider_failure_after_import_does_not_rollback_canonical_portfolio_state()
    {
        if (!RelationalEnabled()) return;
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

    private static bool RelationalEnabled() => Environment.GetEnvironmentVariable("PRUMO_RUN_RELATIONAL_TESTS") == "1";

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string adminConnection;
        private readonly string database;
        private readonly IFormFile file;
        public PrumoDbContext Db { get; }
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
