using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Prumo.Application.Abstractions;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.DocumentSources;
using Prumo.Infrastructure.MarketData;
using Prumo.Infrastructure.Persistence;
using Prumo.Worker;

namespace Prumo.Domain.Tests;

public sealed class DocumentDiscoveryServiceTests
{
    private static readonly DateTimeOffset From = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 8, 31, 23, 59, 59, TimeSpan.Zero);

    [Fact]
    public async Task Persists_only_candidates_inside_interval_and_is_idempotent()
    {
        await using var db = CreateDb();
        var asset = AddPositionedAsset(db, "AAA3");
        var source = new FakeDocumentSource(
            Candidate(asset, "inside", From.AddDays(4)),
            Candidate(asset, "outside", To.AddSeconds(1)));
        var service = CreateService(db, source);

        var first = await service.DiscoverAsync(From, To);
        var second = await service.DiscoverAsync(From, To);

        Assert.Equal(1, first.NewDocuments);
        Assert.Equal(0, first.ExistingDocuments);
        Assert.Equal(1, second.ExistingDocuments);
        Assert.Single(db.Documents);
        Assert.Single(db.ProcessingJobs);
        Assert.Equal(From, source.LastFrom);
        Assert.Equal(To, source.LastTo);
    }

    [Fact]
    public async Task Supports_multiple_assets_and_preserves_document_classification()
    {
        await using var db = CreateDb();
        var firstAsset = AddPositionedAsset(db, "AAA3");
        var secondAsset = AddPositionedAsset(db, "BBB3");
        var source = new FakeDocumentSource(
            Candidate(firstAsset, "relevant", From.AddDays(1), DocumentType.RelevantFact),
            Candidate(secondAsset, "communication", From.AddDays(2), DocumentType.Communication));

        var result = await CreateService(db, source).DiscoverAsync(From, To);

        Assert.Equal(2, result.NewDocuments);
        Assert.Equal(2, db.Documents.Count());
        Assert.Contains(db.Documents, document => document.DocumentType == DocumentType.RelevantFact);
        Assert.Contains(db.Documents, document => document.DocumentType == DocumentType.Communication);
    }

    [Fact]
    public async Task Source_failure_is_propagated_and_does_not_persist_partial_documents()
    {
        await using var db = CreateDb();
        AddPositionedAsset(db, "AAA3");
        var source = new FakeDocumentSource { Failure = new InvalidOperationException("source failure") };

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(db, source).DiscoverAsync(From, To));
        Assert.Empty(db.Documents);
        Assert.Empty(db.ProcessingJobs);
    }

    [Fact]
    public async Task Keeps_operational_issuance_document_without_downloading_or_analyzing()
    {
        await using var db = CreateDb();
        var asset = AddPositionedAsset(db, "AAA3");
        var source = new FakeDocumentSource(new DocumentCandidate(
            asset.Ticker,
            DocumentType.Other,
            From.AddDays(3),
            "issuance-start",
            "https://example.test/issuance-start",
            "https://example.test/issuance-start.pdf",
            "Ofertas Públicas",
            "Anúncio de Início",
            null));

        var result = await CreateService(db, source).DiscoverAsync(From, To);

        Assert.Equal(1, result.NewDocuments);
        Assert.Empty(db.ProcessingJobs);
        var document = Assert.Single(db.Documents);
        Assert.Equal(DocumentProcessingStatus.Ignored, document.ProcessingStatus);
        Assert.Equal("Documento operacional de emissão; mantido apenas como histórico e não enviado para análise.", document.IgnoredReason);
    }

    private static DocumentDiscoveryService CreateService(PrumoDbContext db, FakeDocumentSource source)
    {
        var enrichment = new CvmFiiCnpjEnrichmentService(
            db,
            new HttpClient(new NoopHandler()),
            Options.Create(new CvmFnetOptions()),
            NullLogger<CvmFiiCnpjEnrichmentService>.Instance);
        return new(db, source, enrichment, NullLogger<DocumentDiscoveryService>.Instance);
    }

    private static PrumoDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PrumoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new(options);
    }

    private static Asset AddPositionedAsset(PrumoDbContext db, string ticker)
    {
        var asset = new Asset(ticker, ticker, AssetType.Stock, null, AssetClass.BrazilianStock, "11.111.111/0001-11");
        var portfolio = new Portfolio("Test portfolio");
        db.Assets.Add(asset);
        db.Portfolios.Add(portfolio);
        db.PortfolioPositions.Add(new PortfolioPosition(portfolio.Id, asset.Id, 1, 10, 10));
        db.SaveChanges();
        return asset;
    }

    private static DocumentCandidate Candidate(Asset asset, string id, DateTimeOffset publishedAt, DocumentType type = DocumentType.Other)
        => new(asset.Ticker, type, publishedAt, id, $"https://example.test/{id}", $"https://example.test/{id}.pdf");

    private sealed class FakeDocumentSource(params DocumentCandidate[] candidates) : IDocumentSource
    {
        private readonly IReadOnlyList<DocumentCandidate> candidates = candidates;
        public string Name => "Test source";
        public Exception? Failure { get; init; }
        public DateTimeOffset? LastFrom { get; private set; }
        public DateTimeOffset? LastTo { get; private set; }

        public Task<IReadOnlyList<DocumentCandidate>> FindAsync(IReadOnlyCollection<string> tickers, CancellationToken cancellationToken = default)
            => Task.FromResult(candidates);

        public Task<IReadOnlyList<DocumentCandidate>> FindAsync(IReadOnlyCollection<DocumentSourceAsset> assets, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        {
            LastFrom = from;
            LastTo = to;
            if (Failure is not null) return Task.FromException<IReadOnlyList<DocumentCandidate>>(Failure);
            return Task.FromResult(candidates);
        }
    }

    private sealed class NoopHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }
}
