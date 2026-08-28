using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lodira.Domain.Enums;
using Lodira.Infrastructure.Persistence;

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
    public async Task<ActionResult<IReadOnlyList<DocumentResponse>>> GetPortfolioDocuments(Guid portfolioId, CancellationToken cancellationToken)
    {
        var portfolioExists = await dbContext.Portfolios.AnyAsync(portfolio => portfolio.Id == portfolioId, cancellationToken);
        if (!portfolioExists)
        {
            return NotFound();
        }

        var documents = await dbContext.Documents
            .AsNoTracking()
            .Where(document => dbContext.PortfolioPositions.Any(position => position.PortfolioId == portfolioId && position.AssetId == document.AssetId))
            .Include(document => document.Asset)
            .OrderByDescending(document => document.PublishedAt)
            .Select(document => CreateResponse(document))
            .ToListAsync(cancellationToken);

        return Ok(documents);
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
    public async Task<ActionResult<DocumentAnalysisResponse>> GetLatestAnalysis(Guid documentId, CancellationToken cancellationToken)
    {
        var documentExists = await dbContext.Documents.AnyAsync(item => item.Id == documentId, cancellationToken);
        if (!documentExists)
        {
            return NotFound();
        }

        var analysis = await dbContext.DocumentAnalyses
            .AsNoTracking()
            .Where(item => item.DocumentId == documentId)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return analysis is null ? NotFound() : Ok(CreateAnalysisResponse(analysis));
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

    private static DocumentAnalysisResponse CreateAnalysisResponse(Domain.Entities.DocumentAnalysis analysis)
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
            analysis.CreatedAt);
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
    DateTimeOffset CreatedAt);
