using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lodira.Domain.Enums;
using Lodira.Application.Radar;
using Lodira.Infrastructure.Persistence;
using Lodira.Api.Models;

namespace Lodira.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class DocumentsController : ControllerBase
{
    private readonly LodiraDbContext dbContext;

    public DocumentsController(LodiraDbContext dbContext)
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
        if (type.HasValue) query = query.Where(document => document.DocumentType == type.Value);
        var totalItems = await query.CountAsync(cancellationToken);
        var documents = await query.OrderByDescending(document => document.PublishedAt).ThenByDescending(document => document.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(document => CreateResponse(document)).ToListAsync(cancellationToken);

        return Ok(new PaginatedResponse<DocumentResponse>(documents, page, pageSize, totalItems, (int)Math.Ceiling(totalItems / (double)pageSize)));
    }

    [HttpGet("documents/{documentId:guid}")]
    public async Task<ActionResult<DocumentResponse>> GetDocument(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await dbContext.Documents
            .AsNoTracking()
            .Include(item => item.Asset)
            .SingleOrDefaultAsync(item => item.Id == documentId, cancellationToken);

        return document is null ? NotFound() : Ok(CreateResponse(document));
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
            .SingleOrDefaultAsync(p => p.PortfolioId == portfolioId && p.AssetId == analysis.Document!.AssetId, cancellationToken);
        if (position is null) return null;
        var state = await dbContext.RadarItemStates.AsNoTracking().SingleOrDefaultAsync(s => s.PortfolioId == portfolioId && s.DocumentAnalysisId == analysis.Id, cancellationToken);
        return new PortfolioDocumentAssessmentService().Assess(portfolioId, position, analysis, state?.Status ?? RadarItemStatus.Unread);
    }

    private static DocumentResponse CreateResponse(Domain.Entities.Document document)
    {
        return new DocumentResponse(
            document.Id,
            document.Asset?.Ticker ?? string.Empty,
            document.Asset?.Name ?? string.Empty,
            document.DocumentType,
            document.PublishedAt,
            document.SourceName,
            document.SourceUrl,
            document.FilePath,
            document.ProcessingStatus,
            document.DiscoveredAt,
            document.ProcessedAt);
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
            analysis.PortfolioImpact,
            analysis.AffectedAssets,
            analysis.Confidence,
            analysis.CreatedAt, assessment is null ? null : new PortfolioAssessmentResponse(assessment.PortfolioId, assessment.AssetId, assessment.CurrentValue, assessment.CurrentWeight, assessment.Priority.ToString(), assessment.RequiresAttention, assessment.Reasons.Select(r => r.ToString()).ToList(), assessment.PortfolioImpactText, assessment.RadarStatus.ToString()));
    }
}

public sealed record DocumentResponse(
    Guid Id,
    string Ticker,
    string AssetName,
    DocumentType DocumentType,
    DateTimeOffset PublishedAt,
    string SourceName,
    string SourceUrl,
    string? FilePath,
    DocumentProcessingStatus ProcessingStatus,
    DateTimeOffset DiscoveredAt,
    DateTimeOffset? ProcessedAt);

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
    DateTimeOffset CreatedAt, PortfolioAssessmentResponse? PortfolioAssessment = null);

public sealed record PortfolioAssessmentResponse(Guid PortfolioId, Guid AssetId, decimal? CurrentValue, decimal CurrentWeight, string Priority, bool RequiresAttention, IReadOnlyList<string> Reasons, string PortfolioImpactText, string RadarStatus);
