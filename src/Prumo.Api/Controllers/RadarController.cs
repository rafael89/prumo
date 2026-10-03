using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Application.Radar;
using Prumo.Application.Abstractions;
using Prumo.Application.Portfolio;
using Prumo.Application.Intelligence;
using Prumo.Application.Explainability;
using Prumo.Api.Services;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/radar")]
public sealed class RadarController : ControllerBase
{
    private readonly PrumoDbContext db;
    private readonly IPortfolioAssessmentFreshnessEvaluator freshnessEvaluator;
    private readonly PortfolioAssessmentCurrentService currentAssessmentService;
    public RadarController(PrumoDbContext db, IPortfolioAssessmentFreshnessEvaluator? freshnessEvaluator = null, PortfolioAssessmentCurrentService? currentAssessmentService = null) { this.db = db; this.freshnessEvaluator = freshnessEvaluator ?? new PortfolioAssessmentFreshnessEvaluator(); this.currentAssessmentService = currentAssessmentService ?? new PortfolioAssessmentCurrentService(db, new PortfolioImpactAssessmentService(), this.freshnessEvaluator, Microsoft.Extensions.Logging.Abstractions.NullLogger<PortfolioAssessmentCurrentService>.Instance); }

    [HttpGet]
    public async Task<ActionResult<RadarResponse>> Get([FromQuery] Guid portfolioId, [FromQuery] AttentionPriority? priority, [FromQuery] AssessmentStatus? status, [FromQuery] bool? requiresAttention, [FromQuery] bool notificationPending = false, [FromQuery] string? asset = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default, [FromQuery] bool monitoringOnly = false)
    {
        if (portfolioId == Guid.Empty) return BadRequest("PortfolioId é obrigatório.");
        var query = db.PortfolioAssessments.AsNoTracking().AsSplitQuery().Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets).Include(x => x.InvestmentEvent!).ThenInclude(x => x.Evidence)
            .Include(x => x.InvestmentEvent!).ThenInclude(x => x.DocumentAnalysis!).ThenInclude(x => x.Document)
            .Where(x => x.PortfolioId == portfolioId
                && (x.InvestmentEvent!.DocumentAnalysis!.Importance == AnalysisImportance.Critical
                    || x.InvestmentEvent.DocumentAnalysis.Importance == AnalysisImportance.Relevant));
        if (!string.IsNullOrWhiteSpace(asset)) query = query.Where(x => x.InvestmentEvent!.AffectedAssets.Any(a => EF.Functions.ILike(a.Ticker, $"%{asset.Trim()}%") || EF.Functions.ILike(a.Name, $"%{asset.Trim()}%")));
        var portfolio = await db.Portfolios.AsNoTracking().Include(x => x.Positions).ThenInclude(x => x.Asset).SingleOrDefaultAsync(x => x.Id == portfolioId, ct);
        if (portfolio is null) return NotFound();
        var targets = await db.TargetAllocations.AsNoTracking().Where(x => x.PortfolioId == portfolioId).ToListAsync(ct);
        var loadedAssessments = await query.ToListAsync(ct);
        var currentAssessments = await currentAssessmentService.EnsureFreshAsync(portfolio, targets, loadedAssessments, ct);
        var groupedAssessments = currentAssessments
            .Concat(loadedAssessments.Where(x => x.Status == AssessmentStatus.Closed))
            // The user-facing Radar is document-oriented. A single document may
            // contain several extracted events, but it must not create several
            // cards for the same asset/document.
            .GroupBy(x => x.InvestmentEvent!.DocumentAnalysisId)
            .Where(group => !notificationPending || group.All(x => x.UserState == AssessmentUserState.Unread))
            .Select(group => new RadarGroup(RadarAssessmentSelector.SelectRepresentative(group), BuildEvents(group)))
            .Where(x => !priority.HasValue || x.AttentionPriority == priority.Value)
            .Where(x => !status.HasValue || x.Status == status.Value)
            .OrderByDescending(x => x.Representative.InvestmentEvent!.DocumentAnalysis!.Document!.PublishedAt)
            .ThenByDescending(x => x.Representative.Id)
            .ToList();
        var items = groupedAssessments.Select(x => ToDto(x.Representative, x.Events)).ToList();
        items = items.Where(x => x.RequiresAttention || x.ActionRequirement == ActionRequirement.Monitor || x.Status == AssessmentStatus.Closed).ToList();
        if (requiresAttention.HasValue) items = items.Where(x => x.RequiresAttention == requiresAttention.Value).ToList();
        if (monitoringOnly)
            items = items.Where(x => !x.RequiresAttention && x.ActionRequirement == ActionRequirement.Monitor && x.Status != AssessmentStatus.Closed).ToList();
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 10, 100);
        var total = items.Count; var pageItems = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Ok(new RadarResponse(pageItems.Where(x => x.RequiresAttention).ToList(), pageItems.Where(x => !x.RequiresAttention).ToList(), items.Count(x => x.RequiresAttention), page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize)));
    }

    [HttpPatch("{assessmentId:guid}/dismiss-notification")]
    public async Task<ActionResult<RadarItemDto>> DismissNotification(Guid assessmentId, [FromQuery] Guid portfolioId, CancellationToken ct)
    {
        var assessment = await db.PortfolioAssessments.AsSplitQuery().Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets).Include(x => x.InvestmentEvent!).ThenInclude(x => x.Evidence).Include(x => x.InvestmentEvent!).ThenInclude(x => x.DocumentAnalysis).SingleOrDefaultAsync(x => x.Id == assessmentId && x.PortfolioId == portfolioId, ct);
        if (assessment is null) return NotFound();
        var documentAnalysisId = assessment.InvestmentEvent!.DocumentAnalysisId;
        var relatedAssessments = await db.PortfolioAssessments
            .Include(x => x.InvestmentEvent)
            .Where(x => x.PortfolioId == portfolioId && x.InvestmentEvent!.DocumentAnalysisId == documentAnalysisId)
            .ToListAsync(ct);
        foreach (var related in relatedAssessments) related.MarkSeen();
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(assessment, BuildEvents(relatedAssessments)));
    }

    [HttpPatch("notifications/read-all")]
    public async Task<ActionResult<RadarBulkUpdateResponse>> ReadAllNotifications([FromQuery] Guid portfolioId, CancellationToken ct)
    {
        var assessments = await db.PortfolioAssessments.Where(x => x.PortfolioId == portfolioId && x.UserState == AssessmentUserState.Unread).ToListAsync(ct);
        foreach (var assessment in assessments) assessment.MarkSeen();
        await db.SaveChangesAsync(ct);
        return Ok(new RadarBulkUpdateResponse(assessments.Count));
    }

    [HttpPost("repair")]
    public async Task<ActionResult<RadarRepairResponse>> Repair([FromQuery] Guid portfolioId, CancellationToken ct)
    {
        var portfolioExists = await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct);
        if (!portfolioExists) return NotFound();
            var candidates = await db.DocumentAnalyses
            .Where(analysis => (analysis.Importance == AnalysisImportance.Critical || analysis.Importance == AnalysisImportance.Relevant)
                && db.PortfolioPositions.Any(position => position.PortfolioId == portfolioId && position.AssetId == analysis.Document!.AssetId && position.Quantity > 0)
                && (!db.InvestmentEvents.Any(eventItem => eventItem.DocumentAnalysisId == analysis.Id)
                    || !db.PortfolioAssessments.Any(assessment => assessment.PortfolioId == portfolioId && assessment.InvestmentEvent!.DocumentAnalysisId == analysis.Id)))
            .Select(analysis => analysis.DocumentId)
            .ToListAsync(ct);
        var activeJobs = await db.ProcessingJobs
            .Where(job => job.JobType == ProcessingJobType.AnalyzeDocument
                && (job.Status == ProcessingJobStatus.Queued || job.Status == ProcessingJobStatus.Processing)
                && candidates.Contains(job.DocumentId))
            .Select(job => job.DocumentId)
            .ToHashSetAsync(ct);
        var queued = 0;
        foreach (var documentId in candidates.Where(documentId => !activeJobs.Contains(documentId)))
        {
            db.ProcessingJobs.Add(new ProcessingJob(documentId, ProcessingJobType.AnalyzeDocument, DateTimeOffset.UtcNow));
            queued++;
        }
        await db.SaveChangesAsync(ct);
        return Ok(new RadarRepairResponse(queued));
    }

    [HttpPatch("{assessmentId:guid}/status")]
    public async Task<ActionResult<RadarItemDto>> SetStatus(Guid assessmentId, [FromQuery] Guid portfolioId, [FromBody] SetRadarStatusRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<AssessmentStatus>(request.Status, true, out var status)) return BadRequest("Status inválido.");
        var assessment = await db.PortfolioAssessments.AsSplitQuery().Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets).Include(x => x.InvestmentEvent!).ThenInclude(x => x.Evidence).Include(x => x.InvestmentEvent!).ThenInclude(x => x.DocumentAnalysis).SingleOrDefaultAsync(x => x.Id == assessmentId && x.PortfolioId == portfolioId, ct);
        if (assessment is null) return NotFound();
        var documentAnalysisId = assessment.InvestmentEvent!.DocumentAnalysisId;
        var relatedAssessments = await db.PortfolioAssessments
            .Include(x => x.InvestmentEvent)
            .Where(x => x.PortfolioId == portfolioId && x.InvestmentEvent!.DocumentAnalysisId == documentAnalysisId)
            .ToListAsync(ct);
        foreach (var related in relatedAssessments) related.SetStatus(status);
        await db.SaveChangesAsync(ct); return Ok(ToDto(assessment, BuildEvents(relatedAssessments)));
    }

    [HttpPatch("status-all")]
    public async Task<ActionResult<RadarBulkUpdateResponse>> ResolveAll([FromQuery] Guid portfolioId, CancellationToken ct)
    {
        var assessments = await db.PortfolioAssessments
            .Where(x => x.PortfolioId == portfolioId
                && x.Status != AssessmentStatus.Closed
                && (x.InvestmentEvent!.DocumentAnalysis!.Importance == AnalysisImportance.Critical
                    || x.InvestmentEvent.DocumentAnalysis.Importance == AnalysisImportance.Relevant))
            .ToListAsync(ct);
        foreach (var assessment in assessments) assessment.SetStatus(AssessmentStatus.Closed);
        await db.SaveChangesAsync(ct);
        return Ok(new RadarBulkUpdateResponse(assessments.Count));
    }

    private static IReadOnlyList<RadarEventDto> BuildEvents(IEnumerable<PortfolioAssessment> assessments)
    {
        var representative = RadarAssessmentSelector.SelectRepresentative(assessments);
        var events = assessments
            .Where(x => x.InvestmentEvent is not null)
            .Select(x => x.InvestmentEvent!)
            .GroupBy(x => x.Id)
            .Select(group => group.First())
            .OrderByDescending(x => x.Id == representative.InvestmentEventId)
            .ThenByDescending(x => x.OccurredAt.HasValue)
            .ThenByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new RadarEventDto(x.Id, x.EventType, x.OccurredAt, x.Summary))
            .ToList();
        return events;
    }

    private static RadarItemDto ToDto(PortfolioAssessment x, IReadOnlyList<RadarEventDto> events)
    {
        var attention = RadarAttentionNowPolicy.RequiresAttention(x);
        var eventItem = x.InvestmentEvent!;
        var evidence = eventItem.Evidence.Select(e => new RadarEvidenceDto(e.DocumentId, e.DocumentAnalysisId, e.Reference)).ToList();
        var references = evidence.Select(e => new EvidenceReference("Document", e.DocumentId.ToString(), e.Reference, e.Reference, eventItem.OccurredAt)).ToList();
        return new(x.Id, eventItem.Id, eventItem.DocumentAnalysisId, eventItem.AffectedAssets.Select(a => a.Ticker).ToList(), eventItem.EventType, eventItem.OccurredAt, x.AttentionPriority, x.ActionRequirement, x.CapitalDecisionImpact, x.Status, attention, eventItem.Summary, x.Summary, x.WhyItMatters, x.SuggestedBehavior, x.Confidence, x.GeneratedAt, evidence, FreshnessStatus.Fresh, Array.Empty<FreshnessReason>(), references, eventItem.DocumentAnalysis?.Importance.ToString() ?? "Unknown", events, eventItem.DocumentAnalysis?.Document?.PublishedAt);
    }
}

public sealed record SetRadarStatusRequest(string Status);
public sealed record RadarResponse(IReadOnlyList<RadarItemDto> AttentionNow, IReadOnlyList<RadarItemDto> RecentUpdates, int RequiresAttention, int Page = 1, int PageSize = 10, int TotalItems = 0, int TotalPages = 0);
public sealed record RadarRepairResponse(int QueuedCount);
public sealed record RadarBulkUpdateResponse(int UpdatedCount);
public sealed record RadarEvidenceDto(Guid DocumentId, Guid DocumentAnalysisId, string Reference);
public sealed record RadarEventDto(Guid InvestmentEventId, InvestmentEventType EventType, DateTimeOffset? OccurredAt, string Summary);
public sealed record RadarItemDto(Guid AssessmentId, Guid InvestmentEventId, Guid DocumentAnalysisId, IReadOnlyList<string> AffectedAssets, InvestmentEventType EventType, DateTimeOffset? OccurredAt, AttentionPriority AttentionPriority, ActionRequirement ActionRequirement, CapitalDecisionImpact CapitalDecisionImpact, AssessmentStatus Status, bool RequiresAttention, string EventSummary, string Summary, string WhyItMatters, string SuggestedBehavior, IntelligenceConfidence Confidence, DateTimeOffset GeneratedAt, IReadOnlyList<RadarEvidenceDto> Evidence, FreshnessStatus Freshness = FreshnessStatus.Fresh, IReadOnlyList<FreshnessReason>? FreshnessReasons = null, IReadOnlyList<EvidenceReference>? EvidenceReferences = null, string DocumentImportance = "Unknown", IReadOnlyList<RadarEventDto>? Events = null, DateTimeOffset? DocumentPublishedAt = null);
internal sealed record RadarGroup(PortfolioAssessment Representative, IReadOnlyList<RadarEventDto> Events)
{
    public AttentionPriority AttentionPriority => Representative.AttentionPriority;
    public AssessmentStatus Status => Representative.Status;
}
