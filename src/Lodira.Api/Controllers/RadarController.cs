using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lodira.Api.Models;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Infrastructure.Persistence;
using Lodira.Application.Radar;
using Lodira.Application.Abstractions;
using Lodira.Application.Portfolio;
using Lodira.Application.Intelligence;
using Lodira.Application.Explainability;
using Lodira.Api.Services;

namespace Lodira.Api.Controllers;

[ApiController]
[Route("api/radar")]
public sealed class RadarController : ControllerBase
{
    private readonly LodiraDbContext db;
    private readonly IPortfolioAssessmentFreshnessEvaluator freshnessEvaluator;
    private readonly PortfolioAssessmentCurrentService currentAssessmentService;
    public RadarController(LodiraDbContext db, IPortfolioAssessmentFreshnessEvaluator? freshnessEvaluator = null, PortfolioAssessmentCurrentService? currentAssessmentService = null) { this.db = db; this.freshnessEvaluator = freshnessEvaluator ?? new PortfolioAssessmentFreshnessEvaluator(); this.currentAssessmentService = currentAssessmentService ?? new PortfolioAssessmentCurrentService(db, new PortfolioImpactAssessmentService(), this.freshnessEvaluator, Microsoft.Extensions.Logging.Abstractions.NullLogger<PortfolioAssessmentCurrentService>.Instance); }

    [HttpGet]
    public async Task<ActionResult<RadarResponse>> Get([FromQuery] Guid portfolioId, [FromQuery] AttentionPriority? priority, [FromQuery] AssessmentStatus? status, [FromQuery] AssessmentUserState? userState, [FromQuery] bool? requiresAttention, [FromQuery] string? asset, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
    {
        if (portfolioId == Guid.Empty) return BadRequest("PortfolioId é obrigatório.");
        var query = db.PortfolioAssessments.AsNoTracking().Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets).Include(x => x.InvestmentEvent!).ThenInclude(x => x.Evidence)
            .Where(x => x.PortfolioId == portfolioId);
        if (priority.HasValue) query = query.Where(x => x.AttentionPriority == priority.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (userState.HasValue) query = query.Where(x => x.UserState == userState.Value);
        if (!string.IsNullOrWhiteSpace(asset)) query = query.Where(x => x.InvestmentEvent!.AffectedAssets.Any(a => EF.Functions.ILike(a.Ticker, $"%{asset.Trim()}%") || EF.Functions.ILike(a.Name, $"%{asset.Trim()}%")));
        var portfolio = await db.Portfolios.AsNoTracking().Include(x => x.Positions).ThenInclude(x => x.Asset).SingleOrDefaultAsync(x => x.Id == portfolioId, ct);
        if (portfolio is null) return NotFound();
        var targets = await db.TargetAllocations.AsNoTracking().Where(x => x.PortfolioId == portfolioId).ToListAsync(ct);
        var loadedAssessments = await query.ToListAsync(ct);
        var assessments = (await currentAssessmentService.EnsureFreshAsync(portfolio, targets, loadedAssessments, ct))
            .OrderByDescending(x => x.AttentionPriority)
            .ThenByDescending(x => x.InvestmentEvent!.OccurredAt)
            .ThenByDescending(x => x.GeneratedAt)
            .ThenByDescending(x => x.Id)
            .ToList();
        var items = assessments.Select(ToDto).ToList();
        if (requiresAttention.HasValue) items = items.Where(x => x.RequiresAttention == requiresAttention.Value).ToList();
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 10, 100);
        var total = items.Count; var pageItems = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Ok(new RadarResponse(pageItems.Where(x => x.RequiresAttention).ToList(), pageItems.Where(x => !x.RequiresAttention).ToList(), items.Count(x => x.RequiresAttention), items.Count(x => x.UserState == AssessmentUserState.Unread), page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize)));
    }

    [HttpPatch("{assessmentId:guid}/seen")]
    public async Task<ActionResult<RadarItemDto>> MarkSeen(Guid assessmentId, [FromQuery] Guid portfolioId, CancellationToken ct)
    {
        var assessment = await db.PortfolioAssessments.Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets).Include(x => x.InvestmentEvent!).ThenInclude(x => x.Evidence).SingleOrDefaultAsync(x => x.Id == assessmentId && x.PortfolioId == portfolioId, ct);
        if (assessment is null) return NotFound();
        assessment.MarkSeen(); await db.SaveChangesAsync(ct); return Ok(ToDto(assessment));
    }

    [HttpPatch("{assessmentId:guid}/status")]
    public async Task<ActionResult<RadarItemDto>> SetStatus(Guid assessmentId, [FromQuery] Guid portfolioId, [FromBody] SetRadarStatusRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<AssessmentStatus>(request.Status, true, out var status)) return BadRequest("Status inválido.");
        var assessment = await db.PortfolioAssessments.Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets).Include(x => x.InvestmentEvent!).ThenInclude(x => x.Evidence).SingleOrDefaultAsync(x => x.Id == assessmentId && x.PortfolioId == portfolioId, ct);
        if (assessment is null) return NotFound();
        assessment.SetStatus(status); await db.SaveChangesAsync(ct); return Ok(ToDto(assessment));
    }

    private static RadarItemDto ToDto(PortfolioAssessment x)
    {
        var attention = RadarAttentionNowPolicy.RequiresAttention(x);
        var eventItem = x.InvestmentEvent!;
        var evidence = eventItem.Evidence.Select(e => new RadarEvidenceDto(e.DocumentId, e.DocumentAnalysisId, e.Reference)).ToList();
        var references = evidence.Select(e => new EvidenceReference("Document", e.DocumentId.ToString(), e.Reference, e.Reference, eventItem.OccurredAt)).ToList();
        return new(x.Id, eventItem.Id, eventItem.DocumentAnalysisId, eventItem.AffectedAssets.Select(a => a.Ticker).ToList(), eventItem.EventType, eventItem.OccurredAt, x.AttentionPriority, x.ActionRequirement, x.CapitalDecisionImpact, x.Status, x.UserState, attention, eventItem.Summary, x.Summary, x.WhyItMatters, x.SuggestedBehavior, x.Confidence, x.GeneratedAt, evidence, FreshnessStatus.Fresh, Array.Empty<FreshnessReason>(), references);
    }
}

public sealed record SetRadarStatusRequest(string Status);
public sealed record RadarResponse(IReadOnlyList<RadarItemDto> AttentionNow, IReadOnlyList<RadarItemDto> RecentUpdates, int RequiresAttention, int Unread, int Page = 1, int PageSize = 10, int TotalItems = 0, int TotalPages = 0);
public sealed record RadarEvidenceDto(Guid DocumentId, Guid DocumentAnalysisId, string Reference);
public sealed record RadarItemDto(Guid AssessmentId, Guid InvestmentEventId, Guid DocumentAnalysisId, IReadOnlyList<string> AffectedAssets, InvestmentEventType EventType, DateTimeOffset? OccurredAt, AttentionPriority AttentionPriority, ActionRequirement ActionRequirement, CapitalDecisionImpact CapitalDecisionImpact, AssessmentStatus Status, AssessmentUserState UserState, bool RequiresAttention, string EventSummary, string Summary, string WhyItMatters, string SuggestedBehavior, IntelligenceConfidence Confidence, DateTimeOffset GeneratedAt, IReadOnlyList<RadarEvidenceDto> Evidence, FreshnessStatus Freshness = FreshnessStatus.Fresh, IReadOnlyList<FreshnessReason>? FreshnessReasons = null, IReadOnlyList<EvidenceReference>? EvidenceReferences = null);
