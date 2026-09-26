using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Worker;

namespace Prumo.Domain.Tests;

public sealed class DocumentStorageRepairWorkerTests
{
    [Fact]
    public async Task Existing_file_does_not_reopen_failed_analysis_but_missing_file_is_repaired()
    {
        var services = new ServiceCollection();
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<PrumoDbContext>(options => options.UseInMemoryDatabase(databaseName, databaseRoot));
        services.AddSingleton<IFileStorage>(new TestStorage());
        await using var provider = services.BuildServiceProvider();
        var asset = new Asset("TEST11", "Test asset", AssetType.Fii, "Real Estate");
        var portfolio = new Portfolio("Test portfolio");
        var position = new PortfolioPosition(portfolio.Id, asset.Id, 1m, 1m, 1m);
        var existingFile = NewDocument(asset.Id, "existing.pdf");
        var missingFile = NewDocument(asset.Id, "missing.pdf");
        var failedAnalysis = new ProcessingJob(existingFile.Id, ProcessingJobType.AnalyzeDocument, DateTimeOffset.UtcNow);
        failedAnalysis.Start(DateTimeOffset.UtcNow);
        failedAnalysis.MarkFailedPermanently(DateTimeOffset.UtcNow, "numeric field overflow");
        existingFile.MarkError();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<PrumoDbContext>();
            db.AddRange(asset, portfolio, position, existingFile, missingFile, failedAnalysis);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var worker = new DocumentStorageRepairWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<DocumentStorageRepairWorker>.Instance);
        await worker.RepairAsync(CancellationToken.None);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PrumoDbContext>();
        var persistedAnalysisJob = await verifyDb.ProcessingJobs.SingleAsync(job => job.Id == failedAnalysis.Id, cancellationToken: TestContext.Current.CancellationToken);
        var persistedExistingFile = await verifyDb.Documents.SingleAsync(document => document.Id == existingFile.Id, cancellationToken: TestContext.Current.CancellationToken);
        var persistedMissingFile = await verifyDb.Documents.SingleAsync(document => document.Id == missingFile.Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ProcessingJobStatus.Failed, persistedAnalysisJob.Status);
        Assert.Equal(1, persistedAnalysisJob.Attempts);
        Assert.Equal(DocumentProcessingStatus.Error, persistedExistingFile.ProcessingStatus);
        Assert.Equal("existing.pdf", persistedExistingFile.FilePath);
        Assert.Null(persistedMissingFile.FilePath);
        Assert.Single(await verifyDb.ProcessingJobs.Where(job => job.DocumentId == missingFile.Id && job.JobType == ProcessingJobType.DownloadDocument).ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    private static Document NewDocument(Guid assetId, string path)
    {
        var document = new Document(assetId, DocumentType.RelevantFact, DateTimeOffset.UtcNow, "CVM", path, $"https://example.test/{path}", path);
        document.MarkDownloaded(path, "hash");
        return document;
    }

    private sealed class TestStorage : IFileStorage
    {
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(path == "existing.pdf");
        public Task<StoredFile> SaveAsync(Guid documentId, Stream content, string extension, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
