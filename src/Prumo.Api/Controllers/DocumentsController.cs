using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

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
    public async Task<ActionResult<IReadOnlyList<DocumentResponse>>> GetPortfolioDocuments(Guid portfolioId, CancellationToken cancellationToken)
    {
        var portfolioExists = await dbContext.Portfolios.AnyAsync(portfolio => portfolio.Id == portfolioId, cancellationToken);
        if (!portfolioExists) return NotFound();

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
}

public sealed record DocumentResponse(Guid Id, string Ticker, string AssetName, DocumentType DocumentType, DateTimeOffset PublishedAt, string SourceName, string SourceUrl, string? FilePath, DocumentProcessingStatus ProcessingStatus, DateTimeOffset DiscoveredAt, DateTimeOffset? ProcessedAt);
