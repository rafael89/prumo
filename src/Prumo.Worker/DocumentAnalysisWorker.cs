using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using Prumo.Application.Abstractions;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Ai;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Worker;

public sealed class DocumentAnalysisWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly JobProcessingOptions options;
    private readonly ILogger<DocumentAnalysisWorker> logger;
    private readonly WorkerMetrics metrics;

    public DocumentAnalysisWorker(IServiceScopeFactory scopeFactory, IOptions<JobProcessingOptions> options, ILogger<DocumentAnalysisWorker> logger, WorkerMetrics metrics)
    {
        this.scopeFactory = scopeFactory;
        this.options = options.Value;
        this.logger = logger;
        this.metrics = metrics;
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
                logger.LogError(exception, "Erro inesperado no polling de jobs de analise");
                await Task.Delay(options.PollInterval, stoppingToken);
            }
        }
    }

    private async Task ProcessNextAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PrumoDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<IProcessingJobQueue>();
        var processor = scope.ServiceProvider.GetRequiredService<DocumentAnalysisProcessor>();
        var job = await queue.ReserveNextAsync(DateTimeOffset.UtcNow, options.Lease, options.MaxAttempts, ProcessingJobType.AnalyzeDocument, cancellationToken);
        if (job is null)
        {
            await Task.Delay(options.PollInterval, cancellationToken);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var backlog = await dbContext.ProcessingJobs.CountAsync(item => item.JobType == ProcessingJobType.AnalyzeDocument && item.Status == ProcessingJobStatus.Queued && item.AvailableAt <= DateTimeOffset.UtcNow, cancellationToken);
        metrics.SetBacklog(ProcessingJobType.AnalyzeDocument, backlog);

        try
        {
            if (job.Document is not null && !await HasActivePositionAsync(dbContext, job.Document.AssetId, cancellationToken))
            {
                job.Document.MarkIgnored("O ativo não possui posição positiva na carteira no momento do processamento.");
                job.Complete(DateTimeOffset.UtcNow);
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Job de analise {JobId} ignorado: ativo {AssetId} nao possui posicao positiva.", job.Id, job.Document.AssetId);
                metrics.RecordCompleted(ProcessingJobType.AnalyzeDocument, stopwatch.Elapsed);
                return;
            }

            await processor.ProcessAsync(job, cancellationToken);
            metrics.RecordCompleted(ProcessingJobType.AnalyzeDocument, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Job de analise {JobId} interrompido; sera recuperado apos expiracao do lease.", job.Id);
            throw;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            metrics.RecordFailed(ProcessingJobType.AnalyzeDocument, stopwatch.Elapsed);
            await FailJobAsync(dbContext, job.Id, "Timeout na chamada ao provider de IA.", exception, cancellationToken);
        }
        catch (Exception exception)
        {
            metrics.RecordFailed(ProcessingJobType.AnalyzeDocument, stopwatch.Elapsed);
            await FailJobAsync(dbContext, job.Id, exception.Message, exception, cancellationToken);
        }
    }

    private static Task<bool> HasActivePositionAsync(PrumoDbContext dbContext, Guid assetId, CancellationToken cancellationToken) =>
        dbContext.PortfolioPositions.AnyAsync(position => position.AssetId == assetId && position.Quantity > 0, cancellationToken);

    private async Task FailJobAsync(PrumoDbContext dbContext, Guid jobId, string error, Exception exception, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var job = await dbContext.ProcessingJobs.Include(item => item.Document).SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        if (job is null)
        {
            logger.LogError(exception, "Job de analise {JobId} não foi encontrado após falha.", jobId);
            return;
        }

        if (job.Status != ProcessingJobStatus.Processing)
        {
            logger.LogWarning(exception, "Job de analise {JobId} já está no estado {Status} após falha; nenhum retry foi aplicado.", jobId, job.Status);
            return;
        }

        if (!IsTransient(exception))
        {
            job.MarkFailedPermanently(DateTimeOffset.UtcNow, error);
            if (job.Document is not null) job.Document.MarkError();
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogError(exception, "Falha permanente no job de analise {JobId}; tentativa={Attempt}; erro={Error}", job.Id, job.Attempts, error);
            return;
        }

        var retryDelay = exception is AiProviderRateLimitException { RetryAfter: not null } rateLimit
            ? rateLimit.RetryAfter!.Value
            : ComputeRetryDelay(job.Attempts);
        if (retryDelay > options.MaxRetryDelay) retryDelay = options.MaxRetryDelay;
        var retry = job.Fail(DateTimeOffset.UtcNow, error, options.MaxAttempts, retryDelay);
        if (job.Document is not null) job.Document.MarkError();

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogError(exception, "Falha no job de analise {JobId}; retry={Retry}; tentativa={Attempt}/{MaxAttempts}; erro={Error}", job.Id, retry, job.Attempts, options.MaxAttempts, error);
    }

    private static bool IsTransient(Exception exception) => exception is HttpRequestException or TaskCanceledException or TimeoutException;

    private TimeSpan ComputeRetryDelay(int attempt)
    {
        var multiplier = Math.Pow(2, Math.Max(0, attempt - 1));
        var jitter = 0.8 + (Random.Shared.NextDouble() * 0.4);
        var delay = TimeSpan.FromTicks((long)(options.RetryDelay.Ticks * multiplier * jitter));
        return delay <= options.MaxRetryDelay ? delay : options.MaxRetryDelay;
    }
}
