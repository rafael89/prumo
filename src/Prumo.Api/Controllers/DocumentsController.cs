using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Enums;
using Prumo.Domain.Entities;
using Prumo.Application.Radar;
using Prumo.Application.Documents;
using Prumo.Infrastructure.Persistence;
using Prumo.Api.Models;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class DocumentsController : ControllerBase
{
    private readonly PrumoDbContext dbContext;

    public DocumentsController(PrumoDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    [HttpGet("portfolios/{portfolioId:guid}/documents")]
    public async Task<ActionResult<PaginatedResponse<DocumentResponse>>> GetPortfolioDocuments(Guid portfolioId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? asset = null, [FromQuery] DocumentProcessingStatus? status = null, [FromQuery] DocumentType? type = null, CancellationToken cancellationToken = default)
    {
        var portfolioExists = await dbContext.Portfolios.AnyAsync(portfolio => portfolio.Id == portfolioId, cancellationToken);
        if (!portfolioExists)
        {
            return NotFound();
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 10, 100);
        IQueryable<Domain.Entities.Document> query = dbContext.Documents
            .AsNoTracking()
            .Where(document => dbContext.PortfolioPositions.Any(position => position.PortfolioId == portfolioId && position.AssetId == document.AssetId))
            .Include(document => document.Asset);
        if (!string.IsNullOrWhiteSpace(asset))
        {
            var assetTerm = asset.Trim();
            query = query.Where(document => EF.Functions.ILike(document.Asset!.Ticker, $"%{assetTerm}%") || EF.Functions.ILike(document.Asset.Name, $"%{assetTerm}%"));
        }
        if (status.HasValue) query = query.Where(document => document.ProcessingStatus == status.Value);
        if (type.HasValue) query = ApplyTypeFilter(query, type.Value);
        var totalItems = await query.CountAsync(cancellationToken);
        var pagedDocuments = await query
            .OrderByDescending(document => document.PublishedAt)
            .ThenByDescending(document => document.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var documents = pagedDocuments.Select(CreateResponse).ToList();
        documents = await AttachJobDetailsAsync(documents, cancellationToken);

        return Ok(new PaginatedResponse<DocumentResponse>(documents, page, pageSize, totalItems, (int)Math.Ceiling(totalItems / (double)pageSize)));
    }

    [HttpGet("documents/{documentId:guid}")]
    public async Task<ActionResult<DocumentResponse>> GetDocument(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await dbContext.Documents
            .AsNoTracking()
            .Include(item => item.Asset)
            .SingleOrDefaultAsync(item => item.Id == documentId, cancellationToken);

        if (document is null) return NotFound();
        var response = (await AttachJobDetailsAsync(new[] { CreateResponse(document) }, cancellationToken)).Single();
        return Ok(response);
    }

    private static IQueryable<Domain.Entities.Document> ApplyTypeFilter(IQueryable<Domain.Entities.Document> query, DocumentType type)
    {
        if (type == DocumentType.Other)
        {
            return query.Where(document => document.DocumentType == DocumentType.Other
                && !EF.Functions.ILike(document.SourceCategory ?? "", "%FATO RELEVANTE%")
                && !EF.Functions.ILike(document.SourceType ?? "", "%FATO RELEV%")
                && !EF.Functions.ILike(document.SourceType ?? "", "%COMUNICADO%")
                && !EF.Functions.ILike(document.SourceType ?? "", "%RELAT%GERENCIAL%")
                && !EF.Functions.ILike(document.SourceType ?? "", "%RELATORIO DE GESTAO%")
                && !EF.Functions.ILike(document.SourceType ?? "", "%INFORME MENSAL%")
                && !EF.Functions.ILike(document.SourceType ?? "", "%INFORME TRIMESTRAL%"));
        }

        if (type == DocumentType.RelevantFact)
        {
            return query.Where(document => document.DocumentType == type
                || (document.DocumentType == DocumentType.Other
                    && (EF.Functions.ILike(document.SourceCategory ?? "", "%FATO RELEVANTE%")
                        || EF.Functions.ILike(document.SourceType ?? "", "%FATO RELEV%"))));
        }

        if (type == DocumentType.Communication)
        {
            return query.Where(document => document.DocumentType == type
                || (document.DocumentType == DocumentType.Other && EF.Functions.ILike(document.SourceType ?? "", "%COMUNICADO%")));
        }

        if (type == DocumentType.ManagementReport)
        {
            return query.Where(document => document.DocumentType == type
                || (document.DocumentType == DocumentType.Other && EF.Functions.ILike(document.SourceType ?? "", "%RELAT%GERENCIAL%")));
        }

        if (type == DocumentType.MonthlyReport)
        {
            return query.Where(document => document.DocumentType == type
                || (document.DocumentType == DocumentType.Other && EF.Functions.ILike(document.SourceType ?? "", "%INFORME MENSAL%")));
        }

        if (type == DocumentType.QuarterlyReport)
        {
            return query.Where(document => document.DocumentType == type
                || (document.DocumentType == DocumentType.Other && EF.Functions.ILike(document.SourceType ?? "", "%INFORME TRIMESTRAL%")));
        }

        return query.Where(document => document.DocumentType == type);
    }

    [HttpPost("portfolios/{portfolioId:guid}/documents/reprocess-failed")]
    public async Task<ActionResult<ReprocessFailedDocumentsResponse>> ReprocessFailedDocuments(Guid portfolioId, [FromBody] ReprocessFailedDocumentsRequest? request, CancellationToken cancellationToken)
    {
        if (!await dbContext.Portfolios.AnyAsync(portfolio => portfolio.Id == portfolioId, cancellationToken)) return NotFound();
        var errorFilter = request?.ErrorContains?.Trim();
        var jobs = await dbContext.ProcessingJobs
            .Include(job => job.Document)
            .Where(job => job.JobType == ProcessingJobType.AnalyzeDocument
                && job.Status == ProcessingJobStatus.Failed
                && dbContext.PortfolioPositions.Any(position => position.PortfolioId == portfolioId && position.AssetId == job.Document!.AssetId && position.Quantity > 0)
                && (string.IsNullOrEmpty(errorFilter) || (job.LastError != null && EF.Functions.ILike(job.LastError, $"%{errorFilter}%"))))
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var downloadsQueued = new HashSet<Guid>();
        foreach (var job in jobs)
        {
            job.Requeue(now);
            if (job.Document is null) continue;

            if (string.IsNullOrWhiteSpace(job.Document.FilePath))
            {
                job.Document.MarkPendingDownload();
                var activeDownload = await dbContext.ProcessingJobs.AnyAsync(item => item.DocumentId == job.DocumentId
                    && item.JobType == ProcessingJobType.DownloadDocument
                    && (item.Status == ProcessingJobStatus.Queued || item.Status == ProcessingJobStatus.Processing), cancellationToken);
                if (!activeDownload && downloadsQueued.Add(job.DocumentId)) dbContext.ProcessingJobs.Add(new ProcessingJob(job.DocumentId, ProcessingJobType.DownloadDocument, now));
            }
            else job.Document.MarkWaitingProcessing();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new ReprocessFailedDocumentsResponse(jobs.Count));
    }

    [HttpGet("portfolios/{portfolioId:guid}/document-processing-status")]
    public async Task<ActionResult<ProcessingStatusResponse>> GetDocumentProcessingStatus(Guid portfolioId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Portfolios.AnyAsync(portfolio => portfolio.Id == portfolioId, cancellationToken)) return NotFound();

        var statuses = await dbContext.Documents
            .Where(document => dbContext.PortfolioPositions.Any(position => position.PortfolioId == portfolioId && position.AssetId == document.AssetId))
            .Select(document => new
            {
                document.ProcessingStatus,
                HasActiveJob = dbContext.ProcessingJobs.Any(job => job.DocumentId == document.Id && (job.Status == ProcessingJobStatus.Queued || job.Status == ProcessingJobStatus.Processing))
            })
            .ToListAsync(cancellationToken);
        var processing = statuses.Count(item => item.ProcessingStatus == DocumentProcessingStatus.Processing && item.HasActiveJob);
        var pending = statuses.Count(item => item.HasActiveJob && item.ProcessingStatus is (DocumentProcessingStatus.Discovered or DocumentProcessingStatus.Downloaded or DocumentProcessingStatus.WaitingProcessing));
        var failed = statuses.Count(item => item.ProcessingStatus == DocumentProcessingStatus.Error);
        var state = processing > 0 ? "Processing" : pending > 0 ? "Pending" : failed > 0 ? "Failed" : "Completed";
        var message = state switch
        {
            "Processing" => "Estamos processando seus documentos.",
            "Pending" => "Há documentos aguardando processamento.",
            "Failed" => "Não foi possível processar um ou mais documentos.",
            _ => "Nenhum processamento pendente."
        };
        return Ok(new ProcessingStatusResponse(state, pending + processing, failed, message));
    }

    [HttpGet("documents/{documentId:guid}/analysis")]
    public async Task<ActionResult<DocumentAnalysisResponse>> GetLatestAnalysis(Guid documentId, [FromQuery] Guid? portfolioId, CancellationToken cancellationToken)
    {
        var documentExists = await dbContext.Documents.AnyAsync(item => item.Id == documentId, cancellationToken);
        if (!documentExists)
        {
            return NotFound();
        }

        var analysis = await dbContext.DocumentAnalyses
            .AsNoTracking()
            .Include(item => item.Document!).ThenInclude(item => item!.Asset)
            .Where(item => item.DocumentId == documentId)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (analysis is null) return NotFound();
        var assessment = portfolioId.HasValue
            ? await CreateAssessment(analysis, portfolioId.Value, cancellationToken)
            : null;
        return Ok(CreateAnalysisResponse(analysis, assessment));
    }

    [NonAction]
    public Task<ActionResult<DocumentAnalysisResponse>> GetLatestAnalysis(Guid documentId, CancellationToken cancellationToken)
        => GetLatestAnalysis(documentId, null, cancellationToken);

    private async Task<PortfolioDocumentAssessment?> CreateAssessment(Domain.Entities.DocumentAnalysis analysis, Guid portfolioId, CancellationToken cancellationToken)
    {
        var position = await dbContext.PortfolioPositions.Include(p => p.Asset)
            .SingleOrDefaultAsync(p => p.PortfolioId == portfolioId && p.AssetId == analysis.Document!.AssetId && p.Quantity > 0, cancellationToken);
        if (position is null) return null;
        var persisted = await dbContext.PortfolioAssessments.AsNoTracking()
            .Include(item => item.InvestmentEvent!).ThenInclude(item => item.AffectedAssets)
            .Where(item => item.PortfolioId == portfolioId && item.InvestmentEvent!.DocumentAnalysisId == analysis.Id)
            .OrderByDescending(item => item.GeneratedAt)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (persisted is null) return null;

        var radarStatus = persisted.Status == AssessmentStatus.Closed
            ? RadarItemStatus.Resolved
            : persisted.UserState == AssessmentUserState.Seen ? RadarItemStatus.Read : RadarItemStatus.Unread;
        return new PortfolioDocumentAssessment(
            portfolioId,
            persisted.InvestmentEvent!.AffectedAssets.FirstOrDefault()?.Id ?? position.AssetId,
            analysis.Id,
            position.CurrentValue,
            position.PortfolioWeight,
            RadarAttentionNowPolicy.RequiresAttention(persisted),
            persisted.WhyItMatters,
            (RadarPriority)persisted.AttentionPriority,
            Array.Empty<RadarReason>(),
            radarStatus);
    }

    private static DocumentResponse CreateResponse(Domain.Entities.Document document)
    {
        return new DocumentResponse(
            document.Id,
            document.Asset?.Ticker ?? string.Empty,
            document.Asset?.Name ?? string.Empty,
            document.DocumentType,
            document.CanonicalDocumentType,
            document.SourceCategory,
            document.SourceType,
            document.SourceSpecies,
            document.PublishedAt,
            document.SourceName,
            document.SourceUrl,
            document.FilePath,
            document.ProcessingStatus,
            document.IgnoredReason,
            document.DiscoveredAt,
            document.ProcessedAt);
    }

    private async Task<List<DocumentResponse>> AttachJobDetailsAsync(IEnumerable<DocumentResponse> documents, CancellationToken cancellationToken)
    {
        var responses = documents.ToList();
        var documentIds = responses.Select(document => document.Id).ToArray();
        var jobs = await dbContext.ProcessingJobs.AsNoTracking()
            .Where(job => documentIds.Contains(job.DocumentId))
            .OrderByDescending(job => job.StartedAt ?? job.CompletedAt ?? job.AvailableAt)
            .ToListAsync(cancellationToken);
        return responses.Select(document =>
        {
            if (document.ProcessingStatus == DocumentProcessingStatus.Processed)
            {
                return document with { JobStatus = ProcessingJobStatus.Completed.ToString(), RetryAt = null, LastError = null };
            }

            var documentJobs = jobs.Where(item => item.DocumentId == document.Id).ToList();
            var job = documentJobs.FirstOrDefault(item => item.Status is ProcessingJobStatus.Queued or ProcessingJobStatus.Processing)
                ?? documentJobs.FirstOrDefault(item => item.JobType == ProcessingJobType.AnalyzeDocument);
            return job is null ? document : document with { JobStatus = job.Status.ToString(), Attempts = job.Attempts, RetryAt = job.Status == ProcessingJobStatus.Queued ? job.AvailableAt : null, LastError = job.LastError };
        }).ToList();
    }

    private static DocumentAnalysisResponse CreateAnalysisResponse(Domain.Entities.DocumentAnalysis analysis, PortfolioDocumentAssessment? assessment = null)
    {
        return new DocumentAnalysisResponse(
            analysis.Id,
            analysis.DocumentId,
            analysis.Model,
            analysis.PromptVersion,
            analysis.Importance.ToString(),
            analysis.Classification.ToString(),
            analysis.RequiresAction,
            analysis.SuggestedAction.ToString(),
            analysis.Summary,
            analysis.Impact,
            assessment?.PortfolioImpactText ?? "O impacto na carteira é calculado separadamente para cada carteira.",
            analysis.AffectedAssets,
            analysis.Confidence,
            analysis.CreatedAt,
            analysis.Document!.PublishedAt,
            assessment is null ? null : new PortfolioAssessmentResponse(assessment.PortfolioId, assessment.AssetId, assessment.CurrentValue, assessment.CurrentWeight, assessment.Priority.ToString(), assessment.RequiresAttention, assessment.Reasons.Select(r => r.ToString()).ToList(), assessment.PortfolioImpactText, assessment.RadarStatus.ToString()));
    }
}

public sealed record DocumentResponse(
    Guid Id,
    string Ticker,
    string AssetName,
    DocumentType DocumentType,
    DocumentType CanonicalDocumentType,
    string? SourceCategory,
    string? SourceType,
    string? SourceSpecies,
    DateTimeOffset PublishedAt,
    string SourceName,
    string SourceUrl,
    string? FilePath,
    DocumentProcessingStatus ProcessingStatus,
    string? IgnoredReason,
    DateTimeOffset DiscoveredAt,
    DateTimeOffset? ProcessedAt,
    string? JobStatus = null,
    int Attempts = 0,
    DateTimeOffset? RetryAt = null,
    string? LastError = null);

public sealed record ReprocessFailedDocumentsRequest(string? ErrorContains = null);
public sealed record ReprocessFailedDocumentsResponse(int RequeuedCount);

public sealed record ProcessingStatusResponse(string Status, int ActiveCount, int FailedCount, string Message);

public sealed record DocumentAnalysisResponse(
    Guid Id,
    Guid DocumentId,
    string Model,
    string PromptVersion,
    string Importance,
    string Classification,
    bool RequiresAction,
    string SuggestedAction,
    string Summary,
    string Impact,
    string PortfolioImpact,
    IReadOnlyList<string> AffectedAssets,
    decimal Confidence,
    DateTimeOffset CreatedAt,
    DateTimeOffset PublishedAt,
    PortfolioAssessmentResponse? PortfolioAssessment = null);

public sealed record PortfolioAssessmentResponse(Guid PortfolioId, Guid AssetId, decimal? CurrentValue, decimal CurrentWeight, string Priority, bool RequiresAttention, IReadOnlyList<string> Reasons, string PortfolioImpactText, string RadarStatus);
