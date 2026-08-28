using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lodira.Application.Radar;
using Lodira.Domain.Enums;
using Lodira.Domain.Entities;
using Lodira.Infrastructure.Persistence;

namespace Lodira.Api.Controllers;

[ApiController]
[Route("api/radar")]
public sealed class RadarController : ControllerBase
{
    private readonly LodiraDbContext db;
    private readonly RadarPriorityCalculator calculator = new();
    private readonly ILogger<RadarController> logger;

    public RadarController(LodiraDbContext db, ILogger<RadarController> logger) { this.db = db; this.logger = logger; }

    [HttpGet]
    public async Task<ActionResult<RadarResponse>> Get([FromQuery] RadarPriority? priority, [FromQuery] RadarItemStatus? status,
        [FromQuery] bool? requiresAction, [FromQuery] string? asset, CancellationToken cancellationToken)
    {
        try
        {
            var analyses = await db.DocumentAnalyses.AsNoTracking().Include(a => a.Document!).ThenInclude(d => d!.Asset)
                .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).ToListAsync(cancellationToken);
            var latest = analyses.GroupBy(a => a.DocumentId).Select(g => g.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).First()).ToList();
            var ids = latest.Select(a => a.Id).ToList();
            var states = await db.RadarItemStates.AsNoTracking().Where(s => ids.Contains(s.DocumentAnalysisId)).ToDictionaryAsync(s => s.DocumentAnalysisId, cancellationToken);
            var items = latest.Select(a => ToDto(a, states.TryGetValue(a.Id, out var state) ? state : null)).ToList();
            if (priority.HasValue) items = items.Where(i => i.Priority == priority.Value.ToString()).ToList();
            if (status.HasValue) items = items.Where(i => i.Status == status.Value.ToString()).ToList();
            if (requiresAction.HasValue) items = items.Where(i => i.RequiresAction == requiresAction).ToList();
            if (!string.IsNullOrWhiteSpace(asset)) items = items.Where(i => i.AffectedAssets.Any(x => x.Equals(asset.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
            items = items.OrderBy(i => i.Status == RadarItemStatus.Resolved.ToString()).ThenByDescending(i => PriorityRank(i.Priority)).ThenByDescending(i => i.Score)
                .ThenByDescending(i => i.RequiresAction).ThenByDescending(i => i.PublishedAt).ThenByDescending(i => i.DocumentAnalysisId).ToList();
            return Ok(new RadarResponse(items, items.Count(i => i.Status == RadarItemStatus.Unread.ToString()), items.Count(RequiresAttention), items.Count(i => i.Status == RadarItemStatus.Resolved.ToString())));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Erro ao consultar o Radar");
            throw;
        }
    }

    [HttpPatch("{analysisId:guid}/status")]
    public async Task<ActionResult<RadarItemDto>> SetStatus(Guid analysisId, [FromBody] SetRadarStatusRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<RadarItemStatus>(request.Status, true, out var requestedStatus)) return BadRequest("Status inválido.");
        var analysis = await db.DocumentAnalyses.Include(a => a.Document!).ThenInclude(d => d!.Asset).SingleOrDefaultAsync(a => a.Id == analysisId, cancellationToken);
        if (analysis is null) return NotFound();
        var state = await db.RadarItemStates.SingleOrDefaultAsync(s => s.DocumentAnalysisId == analysisId, cancellationToken);
        if (state is null) { state = new RadarItemState(analysisId); db.RadarItemStates.Add(state); }
        state.SetStatus(requestedStatus);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Estado do item do Radar alterado para {Status}", request.Status);
        return Ok(ToDto(analysis, state));
    }

    private RadarItemDto ToDto(DocumentAnalysis a, RadarItemState? state)
    {
        var result = calculator.Calculate(a);
        var status = state?.Status ?? RadarItemStatus.Unread;
        return new RadarItemDto(a.DocumentId, a.Id, a.Document?.Asset?.Ticker ?? string.Empty, a.Document?.DocumentType ?? DocumentType.Other, a.Document?.PublishedAt,
            result.Priority.ToString(), result.Score, result.Reasons.Select(reason => reason.ToString()).ToList(), status.ToString(), status != RadarItemStatus.Resolved && (result.Priority >= RadarPriority.High || a.RequiresAction),
            a.Importance.ToString(), a.Classification.ToString(), a.RequiresAction, a.SuggestedAction.ToString(), a.Summary, a.PortfolioImpact, a.Confidence, a.AffectedAssets, a.CreatedAt);
    }
    private static bool RequiresAttention(RadarItemDto item) => item.RequiresAttention;
    private static int PriorityRank(string priority) => priority switch { nameof(RadarPriority.Critical) => 4, nameof(RadarPriority.High) => 3, nameof(RadarPriority.Medium) => 2, _ => 1 };
}

public sealed record SetRadarStatusRequest(string Status);
public sealed record RadarResponse(IReadOnlyList<RadarItemDto> Items, int Unread, int RequiresAttention, int Resolved);
public sealed record RadarItemDto(Guid DocumentId, Guid DocumentAnalysisId, string AssetTicker, DocumentType DocumentType, DateTimeOffset? PublishedAt,
    string Priority, int Score, IReadOnlyList<string> Reasons, string Status, bool RequiresAttention,
    string Importance, string Classification, bool RequiresAction, string SuggestedAction,
    string Summary, string PortfolioImpact, decimal Confidence, IReadOnlyList<string> AffectedAssets, DateTimeOffset CreatedAt);
