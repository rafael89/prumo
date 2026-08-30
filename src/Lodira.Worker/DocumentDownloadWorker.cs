using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Lodira.Application.Abstractions;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Infrastructure.Persistence;

namespace Lodira.Worker;

public sealed class DocumentDownloadWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly JobProcessingOptions options;
    private readonly ILogger<DocumentDownloadWorker> logger;

    public DocumentDownloadWorker(IServiceScopeFactory scopeFactory, IHttpClientFactory httpClientFactory, IOptions<JobProcessingOptions> options, ILogger<DocumentDownloadWorker> logger)
    {
        this.scopeFactory = scopeFactory;
        this.httpClientFactory = httpClientFactory;
        this.options = options.Value;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Erro inesperado no polling de jobs");
                await Task.Delay(options.PollInterval, stoppingToken);
            }
        }
    }

    private async Task ProcessNextAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LodiraDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<IProcessingJobQueue>();
        var job = await queue.ReserveNextAsync(DateTimeOffset.UtcNow, options.Lease, options.MaxAttempts, ProcessingJobType.DownloadDocument, cancellationToken);
        if (job is null)
        {
            await Task.Delay(options.PollInterval, cancellationToken);
            return;
        }

        try
        {
            await DownloadAsync(job, dbContext, scope.ServiceProvider.GetRequiredService<IFileStorage>(), cancellationToken);
            job.Document!.MarkWaitingProcessing();
            var analysisExists = await dbContext.ProcessingJobs.AnyAsync(
                item => item.DocumentId == job.DocumentId
                    && item.JobType == ProcessingJobType.AnalyzeDocument,
                cancellationToken);
            if (!analysisExists)
            {
                dbContext.ProcessingJobs.Add(new ProcessingJob(job.DocumentId, ProcessingJobType.AnalyzeDocument, DateTimeOffset.UtcNow));
            }
            job.Complete(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Job {JobId} concluído para documento {DocumentId}", job.Id, job.DocumentId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Job {JobId} interrompido; será recuperado após expiração do lease", job.Id);
            throw;
        }
        catch (Exception exception)
        {
            dbContext.ChangeTracker.Clear();
            var persistedJob = await dbContext.ProcessingJobs.Include(item => item.Document).SingleOrDefaultAsync(item => item.Id == job.Id, cancellationToken);
            if (persistedJob is null || persistedJob.Status != ProcessingJobStatus.Processing)
            {
                logger.LogWarning(exception, "Job {JobId} não está disponível para retry após falha.", job.Id);
                return;
            }

            var retry = persistedJob.Fail(DateTimeOffset.UtcNow, exception.Message, options.MaxAttempts, options.RetryDelay);
            if (persistedJob.Document is not null) persistedJob.Document.MarkError();
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogError(exception, "Falha no job {JobId}; retry={Retry}; tentativa={Attempt}/{MaxAttempts}", persistedJob.Id, retry, persistedJob.Attempts, options.MaxAttempts);
        }
    }

    private async Task DownloadAsync(ProcessingJob job, LodiraDbContext dbContext, IFileStorage storage, CancellationToken cancellationToken)
    {
        if (job.Document is null || string.IsNullOrWhiteSpace(job.Document.DownloadUrl))
        {
            throw new InvalidOperationException("Documento sem URL de download.");
        }

        var client = httpClientFactory.CreateClient("documents");
        using var response = await client.GetAsync(job.Document.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var storedFile = await storage.SaveAsync(job.Document.Id, content, "pdf", cancellationToken);
        var duplicate = await dbContext.Documents.AsNoTracking()
            .Where(document => document.Id != job.Document.Id && document.FileHash == storedFile.Hash && document.FilePath != null)
            .Select(document => document.FilePath)
            .FirstOrDefaultAsync(cancellationToken);
        if (duplicate is not null && File.Exists(duplicate))
        {
            await storage.DeleteAsync(storedFile.Path, cancellationToken);
            storedFile = new StoredFile(duplicate, storedFile.Hash);
            logger.LogInformation("Conteúdo duplicado por hash; documento {DocumentId} reutilizará {FilePath}.", job.Document.Id, duplicate);
        }
        job.Document.MarkDownloaded(storedFile.Path, storedFile.Hash);
    }

}

public sealed class JobProcessingOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(30);
    public int MaxAttempts { get; set; } = 3;
}
