using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Lodira.Application.Abstractions;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Infrastructure.Persistence;
using Lodira.Infrastructure.PortfolioImports;

namespace Lodira.Domain.Tests;

public sealed class NewAssetBootstrapServiceTests
{
    [Fact]
    public async Task Global_asset_exists_but_new_portfolio_position_schedules_bootstrap()
    {
        var options = new DbContextOptionsBuilder<LodiraDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new LodiraDbContext(options);
        var asset = new Asset("XPML11", "XP Malls", AssetType.Fii, null, AssetClass.Fii, "12345678000190");
        var portfolioA = new Portfolio("A");
        var portfolioB = new Portfolio("B");
        db.AddRange(asset, portfolioA, portfolioB);
        db.PortfolioPositions.Add(new PortfolioPosition(portfolioA.Id, asset.Id, 1, 0, 100));
        await db.SaveChangesAsync();

        var source = new FakeSource();
        var service = new NewAssetBootstrapService(db, source, NullLogger<NewAssetBootstrapService>.Instance);
        await service.DiscoverLatestOnlyAsync(new[] { asset.Id }, CancellationToken.None);

        Assert.Equal("12345678000190", source.Received.Single().Cnpj);
        Assert.Single(db.Documents);
        Assert.Single(db.ProcessingJobs);
        Assert.Equal(ProcessingJobType.DownloadDocument, db.ProcessingJobs.Single().JobType);
        Assert.Equal(0, db.PortfolioPositions.Count(position => position.PortfolioId == portfolioB.Id));
    }

    [Fact]
    public async Task Bootstrap_selects_only_latest_document_for_each_asset()
    {
        var options = new DbContextOptionsBuilder<LodiraDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new LodiraDbContext(options);
        var asset = new Asset("XPML11", "XP Malls", AssetType.Fii, null, AssetClass.Fii, "12345678000190");
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        var service = new NewAssetBootstrapService(db, new MultipleDocumentsSource(), NullLogger<NewAssetBootstrapService>.Instance);
        await service.DiscoverLatestOnlyAsync(new[] { asset.Id }, CancellationToken.None);

        var document = Assert.Single(db.Documents);
        Assert.Equal(DateTimeOffset.Parse("2026-07-01T00:00:00Z"), document.PublishedAt);
        Assert.Single(db.ProcessingJobs);
        Assert.Equal(ProcessingJobType.DownloadDocument, db.ProcessingJobs.Single().JobType);
    }

    [Fact]
    public async Task Bootstrap_selects_context_by_priority_and_recent_material_fact()
    {
        var options = new DbContextOptionsBuilder<LodiraDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new LodiraDbContext(options);
        var asset = new Asset("XPML11", "XP Malls", AssetType.Fii, null, AssetClass.Fii, "12345678000190");
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        var service = new NewAssetBootstrapService(db, new BootstrapDocumentsSource(), NullLogger<NewAssetBootstrapService>.Instance);
        await service.DiscoverLatestOnlyAsync(new[] { asset.Id }, CancellationToken.None);

        Assert.Equal(2, db.Documents.Count());
        Assert.Contains(db.Documents, document => document.DocumentType == DocumentType.ManagementReport);
        Assert.Contains(db.Documents, document => document.DocumentType == DocumentType.RelevantFact);
    }

    private sealed class FakeSource : IDocumentSource
    {
        public string Name => "Fake CVM";
        public List<DocumentSourceAsset> Received { get; } = [];
        public Task<IReadOnlyList<DocumentCandidate>> FindAsync(IReadOnlyCollection<string> tickers, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DocumentCandidate>>([]);
        public Task<IReadOnlyList<DocumentCandidate>> FindAsync(IReadOnlyCollection<DocumentSourceAsset> assets, CancellationToken cancellationToken = default)
        {
            Received.AddRange(assets);
            return Task.FromResult<IReadOnlyList<DocumentCandidate>>([
                new("XPML11", DocumentType.ManagementReport, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), "doc-1", "https://example.test/doc-1.pdf", "https://example.test/doc-1.pdf")]);
        }
    }

    private sealed class MultipleDocumentsSource : IDocumentSource
    {
        public string Name => "Fake CVM";
        public Task<IReadOnlyList<DocumentCandidate>> FindAsync(IReadOnlyCollection<string> tickers, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DocumentCandidate>>([]);
        public Task<IReadOnlyList<DocumentCandidate>> FindAsync(IReadOnlyCollection<DocumentSourceAsset> assets, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DocumentCandidate>>([
            new("XPML11", DocumentType.ManagementReport, DateTimeOffset.Parse("2026-01-01T00:00:00Z"), "a", "https://example.test/a.pdf", "https://example.test/a.pdf"),
            new("XPML11", DocumentType.ManagementReport, DateTimeOffset.Parse("2026-07-01T00:00:00Z"), "b", "https://example.test/b.pdf", "https://example.test/b.pdf")]);
    }

    private sealed class BootstrapDocumentsSource : IDocumentSource
    {
        public string Name => "Fake CVM";
        public Task<IReadOnlyList<DocumentCandidate>> FindAsync(IReadOnlyCollection<string> tickers, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DocumentCandidate>>([]);
        public Task<IReadOnlyList<DocumentCandidate>> FindAsync(IReadOnlyCollection<DocumentSourceAsset> assets, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DocumentCandidate>>([
            new("XPML11", DocumentType.Other, DateTimeOffset.Parse("2026-08-26T00:00:00Z"), "notice", "https://example.test/notice", "https://example.test/notice", "Avisos", "Aviso operacional", null),
            new("XPML11", DocumentType.MonthlyReport, DateTimeOffset.Parse("2026-08-20T00:00:00Z"), "monthly", "https://example.test/monthly", "https://example.test/monthly", "Informes Periódicos", "Informe Mensal", null),
            new("XPML11", DocumentType.ManagementReport, DateTimeOffset.Parse("2026-08-05T00:00:00Z"), "management", "https://example.test/management", "https://example.test/management", "Relatórios", "Relatório Gerencial", null),
            new("XPML11", DocumentType.RelevantFact, DateTimeOffset.Parse("2026-08-10T00:00:00Z"), "fact", "https://example.test/fact", "https://example.test/fact", "Fatos Relevantes", "Fato Relevante", null)]);
    }
}
