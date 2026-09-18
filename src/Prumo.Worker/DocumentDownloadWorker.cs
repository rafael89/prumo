using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text;
using Prumo.Application.Abstractions;
using Prumo.Application.PortfolioImports;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Documents;

namespace Prumo.Worker;

public sealed class DocumentDownloadWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly JobProcessingOptions options;
    private readonly ILogger<DocumentDownloadWorker> logger;
    private readonly WorkerMetrics metrics;
    private readonly DocumentRetentionOptions retention;

    public DocumentDownloadWorker(IServiceScopeFactory scopeFactory, IHttpClientFactory httpClientFactory, IOptions<JobProcessingOptions> options, ILogger<DocumentDownloadWorker> logger, WorkerMetrics metrics, IOptions<DocumentRetentionOptions> retention)
    {
        this.scopeFactory = scopeFactory;
        this.httpClientFactory = httpClientFactory;
        this.options = options.Value;
        this.logger = logger;
        this.metrics = metrics;
        this.retention = retention.Value;
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
        var dbContext = scope.ServiceProvider.GetRequiredService<PrumoDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<IProcessingJobQueue>();
        var job = await queue.ReserveNextAsync(DateTimeOffset.UtcNow, options.Lease, options.MaxAttempts, ProcessingJobType.DownloadDocument, cancellationToken);
        if (job is null)
        {
            await Task.Delay(options.PollInterval, cancellationToken);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var backlog = await dbContext.ProcessingJobs.CountAsync(item => item.JobType == ProcessingJobType.DownloadDocument && item.Status == ProcessingJobStatus.Queued && item.AvailableAt <= DateTimeOffset.UtcNow, cancellationToken);
        metrics.SetBacklog(ProcessingJobType.DownloadDocument, backlog);

        try
        {
            if (job.Document is not null && !await HasActivePositionAsync(dbContext, job.Document.AssetId, cancellationToken))
            {
                job.Document.MarkIgnored("O ativo não possui posição positiva na carteira no momento do processamento.");
                job.Complete(DateTimeOffset.UtcNow);
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Job de download {JobId} ignorado: ativo {AssetId} nao possui posicao positiva.", job.Id, job.Document.AssetId);
                metrics.RecordCompleted(ProcessingJobType.DownloadDocument, stopwatch.Elapsed);
                return;
            }

            await DownloadAsync(job, dbContext, scope.ServiceProvider.GetRequiredService<IFileStorage>(), cancellationToken);
            job.Document!.MarkWaitingProcessing();
            var analysisExists = await dbContext.DocumentAnalyses.AnyAsync(
                item => item.DocumentId == job.DocumentId,
                cancellationToken);
            var analysisJobActive = await dbContext.ProcessingJobs.AnyAsync(
                item => item.DocumentId == job.DocumentId
                    && item.JobType == ProcessingJobType.AnalyzeDocument
                    && (item.Status == ProcessingJobStatus.Queued || item.Status == ProcessingJobStatus.Processing),
                cancellationToken);
            if (!analysisExists && !analysisJobActive)
            {
                dbContext.ProcessingJobs.Add(new ProcessingJob(job.DocumentId, ProcessingJobType.AnalyzeDocument, DateTimeOffset.UtcNow));
            }
            else if (analysisExists && !analysisJobActive)
            {
                // O documento já foi analisado anteriormente; a recuperação do PDF encerra o fluxo.
                job.Document.MarkProcessed();
            }
            job.Complete(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (analysisExists && !analysisJobActive && job.Document is not null)
            {
                await ReleasePdfAfterExistingAnalysisAsync(job.Document, dbContext, scope.ServiceProvider.GetRequiredService<IFileStorage>(), cancellationToken);
            }
            logger.LogInformation("Job {JobId} concluído para documento {DocumentId}; duraçãoMs={DurationMs}; backlog={Backlog}", job.Id, job.DocumentId, stopwatch.Elapsed.TotalMilliseconds, backlog);
            metrics.RecordCompleted(ProcessingJobType.DownloadDocument, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Job {JobId} interrompido; será recuperado após expiração do lease", job.Id);
            throw;
        }
        catch (Exception exception)
        {
            metrics.RecordFailed(ProcessingJobType.DownloadDocument, stopwatch.Elapsed);
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
            if (retry)
            {
                logger.LogWarning(exception, "Falha transitória no job {JobId}; retry=true; tentativa={Attempt}/{MaxAttempts}", persistedJob.Id, persistedJob.Attempts, options.MaxAttempts);
            }
            else
            {
                logger.LogError(exception, "Falha permanente no job {JobId}; retry=false; tentativa={Attempt}/{MaxAttempts}", persistedJob.Id, persistedJob.Attempts, options.MaxAttempts);
            }
        }
    }

    private async Task ReleasePdfAfterExistingAnalysisAsync(Document document, PrumoDbContext dbContext, IFileStorage storage, CancellationToken cancellationToken)
    {
        if (!retention.DeletePdfAfterAnalysis || string.IsNullOrWhiteSpace(document.FilePath)) return;
        var path = document.FilePath;
        var shared = await dbContext.Documents.AsNoTracking().AnyAsync(item => item.Id != document.Id && item.FilePath == path, cancellationToken);
        if (!shared) await storage.DeleteAsync(path, cancellationToken);
        document.ReleaseStoredFile();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task DownloadAsync(ProcessingJob job, PrumoDbContext dbContext, IFileStorage storage, CancellationToken cancellationToken)
    {
        if (job.Document is null || string.IsNullOrWhiteSpace(job.Document.DownloadUrl))
        {
            throw new InvalidOperationException("Documento sem URL de download.");
        }

        var client = httpClientFactory.CreateClient("documents");
        using var request = new HttpRequestMessage(HttpMethod.Get, job.Document.DownloadUrl);
        if (Uri.TryCreate(job.Document.DownloadUrl, UriKind.Absolute, out var downloadUri)
            && downloadUri.Host.Contains("fnet.bmfbovespa.com.br", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.TryAddWithoutValidation("Accept", "application/pdf,application/octet-stream,*/*");
            request.Headers.TryAddWithoutValidation("Referer", $"https://fnet.bmfbovespa.com.br/fnet/publico/exibirDocumento?id={Uri.EscapeDataString(GetFnetDocumentId(downloadUri))}&cvm=true");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0 Safari/537.36");
        }
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!DocumentPayloadDetector.TryDetect(payload, response.Content.Headers.ContentType?.MediaType, out var payloadKind))
        {
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "<ausente>";
            var preview = Encoding.UTF8.GetString(payload, 0, Math.Min(payload.Length, 120))
                .Replace('\r', ' ')
                .Replace('\n', ' ');
            throw new InvalidOperationException($"Download do documento não retornou PDF/XML válido; contentType={contentType}; preview={preview}");
        }

        await using var content = new MemoryStream(payload, writable: false);
        var extension = payloadKind == DocumentPayloadKind.Xml ? "xml" : "pdf";
        var storedFile = await storage.SaveAsync(job.Document.Id, content, extension, cancellationToken);
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

    private static Task<bool> HasActivePositionAsync(PrumoDbContext dbContext, Guid assetId, CancellationToken cancellationToken) =>
        dbContext.PortfolioPositions.AnyAsync(position => position.AssetId == assetId && position.Quantity > 0, cancellationToken);

    private static string GetFnetDocumentId(Uri uri) =>
        uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(parameter => parameter.Split('=', 2))
            .Where(parameter => parameter.Length == 2 && string.Equals(parameter[0], "id", StringComparison.OrdinalIgnoreCase))
            .Select(parameter => Uri.UnescapeDataString(parameter[1]))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

}

public sealed class JobProcessingOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(30);
    public int MaxAttempts { get; set; } = 3;
}
