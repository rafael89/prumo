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
    public async Task<ActionResult<RadarResponse>> Get([FromQuery] Guid portfolioId, [FromQuery] AttentionPriority? priority, [FromQuery] AssessmentStatus? status, [FromQuery] bool? requiresAttention, [FromQuery] bool notificationPending = false, [FromQuery] string? asset = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
    {
        if (portfolioId == Guid.Empty) return BadRequest("PortfolioId é obrigatório.");
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 10, 100);
        var portfolio = await db.Portfolios.AsNoTracking().Include(x => x.Positions).ThenInclude(x => x.Asset).SingleOrDefaultAsync(x => x.Id == portfolioId, ct);
        if (portfolio is null) return NotFound();
        var targets = await db.TargetAllocations.AsNoTracking().Where(x => x.PortfolioId == portfolioId).ToListAsync(ct);
        var query = db.PortfolioAssessments.AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId && x.InvestmentEvent!.DocumentAnalysis!.Document != null
                && (x.InvestmentEvent.DocumentAnalysis.Importance == AnalysisImportance.Critical
                    || x.InvestmentEvent.DocumentAnalysis.Importance == AnalysisImportance.Relevant
                    || x.InvestmentEvent.RadarDetails != null));
        if (!string.IsNullOrWhiteSpace(asset)) query = query.Where(x => x.InvestmentEvent!.AffectedAssets.Any(a => EF.Functions.ILike(a.Ticker, $"%{asset.Trim()}%") || EF.Functions.ILike(a.Name, $"%{asset.Trim()}%")));
        var reader = new PortfolioAssessmentReader(db, currentAssessmentService, freshnessEvaluator);
        var rows = await reader.ReadAsync(query, portfolio, targets, ct);
        var groups = rows.Where(x => RadarEventPolicy.Importance(x.Event, x.Importance) is AnalysisImportance.Critical or AnalysisImportance.Relevant)
            .GroupBy(x => x.CardId)
            .Where(group => !notificationPending || group.All(x => x.Assessment.UserState == AssessmentUserState.Unread))
            .Select(group => new { Rows = group.ToList(), Representative = RadarAssessmentSelector.SelectRepresentative(group.Select(x => x.Assessment)) })
            .Where(x => !priority.HasValue || x.Representative.AttentionPriority == priority)
            .Where(x => !status.HasValue || x.Representative.Status == status)
            .Where(x => RadarAttentionNowPolicy.RequiresAttention(x.Representative) || x.Representative.Status == AssessmentStatus.Closed)
            .Where(x => !requiresAttention.HasValue || RadarAttentionNowPolicy.RequiresAttention(x.Representative) == requiresAttention)
            .OrderByDescending(x => x.Rows.Max(r => r.PublishedAt))
            .ThenByDescending(x => x.Representative.Id).ToList();
        var total = groups.Count;
        var selected = groups.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var ids = selected.SelectMany(x => x.Rows).Select(x => x.Assessment.Id).ToArray();
        var details = (await reader.LoadDetailsAsync(ids, ct)).ToDictionary(x => x.Id);
        var pageItems = selected.Select(group =>
        {
            var representative = details[group.Representative.Id];
            if (group.Rows.Any(x => x.Assessment.Id == representative.Id && x.LegacyLiquidation)) representative.OverrideCapitalDecisionImpact(CapitalDecisionImpact.Block);
            return ToDto(representative, BuildEvents(group.Rows.Select(x => details[x.Assessment.Id])), group.Rows.Select(x => details[x.Assessment.Id]));
        }).ToList();
        return Ok(new RadarResponse(pageItems.Where(x => x.RequiresAttention).ToList(), pageItems.Where(x => !x.RequiresAttention).ToList(), groups.Count(x => RadarAttentionNowPolicy.RequiresAttention(x.Representative)), page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize)));
    }

    [HttpGet("notifications/version")]
    public async Task<ActionResult<object>> GetNotificationVersion([FromQuery] Guid portfolioId, CancellationToken ct)
    {
        if (!await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct)) return NotFound();
        var states = await db.PortfolioAssessments.AsNoTracking().Where(x => x.PortfolioId == portfolioId)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.UserState, x.GeneratedAt, x.InvestmentEventVersion, EventVersion = x.InvestmentEvent!.Version }).ToListAsync(ct);
        var positions = await db.PortfolioPositions.AsNoTracking().Where(x => x.PortfolioId == portfolioId)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.UpdatedAt, x.Asset!.AssetClass }).ToListAsync(ct);
        var targets = await db.TargetAllocations.AsNoTracking().Where(x => x.PortfolioId == portfolioId).OrderBy(x => x.AssetClass)
            .Select(x => new { x.AssetClass, x.TargetPercentage }).ToListAsync(ct);
        var canonical = System.Text.Json.JsonSerializer.Serialize(new { states, positions, targets, Policy = PortfolioImpactAssessmentService.CurrentVersion });
        var version = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));
        return Ok(new { version });
    }

    [HttpPatch("{assessmentId:guid}/dismiss-notification")]
    public async Task<ActionResult<RadarItemDto>> DismissNotification(Guid assessmentId, [FromQuery] Guid portfolioId, CancellationToken ct)
    {
        var assessment = await db.PortfolioAssessments.AsSplitQuery().Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets).Include(x => x.InvestmentEvent!).ThenInclude(x => x.Evidence).Include(x => x.InvestmentEvent!).ThenInclude(x => x.DocumentAnalysis).SingleOrDefaultAsync(x => x.Id == assessmentId && x.PortfolioId == portfolioId, ct);
        if (assessment is null) return NotFound();
        var relatedAssessments = await RelatedAssessments(assessment, portfolioId, ct);
        foreach (var related in relatedAssessments) related.MarkSeen();
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(assessment, BuildEvents(relatedAssessments), relatedAssessments));
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
            .Where(analysis => analysis.PromptVersion == Prumo.Application.Documents.DocumentAnalysisPrompt.GlobalVersion
                && (analysis.Importance == AnalysisImportance.Critical || analysis.Importance == AnalysisImportance.Relevant)
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
        var relatedAssessments = await RelatedAssessments(assessment, portfolioId, ct);
        foreach (var related in relatedAssessments) related.SetStatus(status);
        await db.SaveChangesAsync(ct); return Ok(ToDto(assessment, BuildEvents(relatedAssessments), relatedAssessments));
    }

    [HttpPatch("status-all")]
    public async Task<ActionResult<RadarBulkUpdateResponse>> ResolveAll([FromQuery] Guid portfolioId, CancellationToken ct)
    {
        var assessments = (await db.PortfolioAssessments
            .Include(x => x.InvestmentEvent!).ThenInclude(x => x.DocumentAnalysis)
            .Where(x => x.PortfolioId == portfolioId
                && x.Status != AssessmentStatus.Closed
                && (x.InvestmentEvent!.DocumentAnalysis!.Importance == AnalysisImportance.Critical
                    || x.InvestmentEvent.DocumentAnalysis.Importance == AnalysisImportance.Relevant
                    || x.InvestmentEvent.RadarDetails != null))
            .ToListAsync(ct))
            .Where(x => RadarEventPolicy.Importance(x.InvestmentEvent!) is AnalysisImportance.Critical or AnalysisImportance.Relevant)
            .ToList();
        assessments = assessments.Where(RadarAttentionNowPolicy.RequiresAttention).ToList();
        var cards = await RadarCardReader.ReadAsync(db, portfolioId, ct);
        var selectedCards = assessments.Select(x => cards.GetValueOrDefault(x.InvestmentEvent!.DocumentAnalysisId, x.InvestmentEvent.DocumentAnalysisId)).ToHashSet();
        var analysisIds = cards.Where(x => selectedCards.Contains(x.Value)).Select(x => x.Key)
            .Concat(assessments.Select(x => x.InvestmentEvent!.DocumentAnalysisId)).Distinct().ToArray();
        assessments = await db.PortfolioAssessments
            .Where(x => x.PortfolioId == portfolioId && x.Status != AssessmentStatus.Closed
                && analysisIds.Contains(x.InvestmentEvent!.DocumentAnalysisId))
            .ToListAsync(ct);
        foreach (var assessment in assessments) assessment.SetStatus(AssessmentStatus.Closed);
        await db.SaveChangesAsync(ct);
        return Ok(new RadarBulkUpdateResponse(assessments.Count));
    }

    private async Task<List<PortfolioAssessment>> RelatedAssessments(PortfolioAssessment assessment, Guid portfolioId, CancellationToken ct)
    {
        var cards = await RadarCardReader.ReadAsync(db, portfolioId, ct);
        var analysisId = assessment.InvestmentEvent!.DocumentAnalysisId;
        var cardId = cards.GetValueOrDefault(analysisId, analysisId);
        var analysisIds = cards.Where(x => x.Value == cardId).Select(x => x.Key).Append(analysisId).Distinct().ToArray();
        var related = await db.PortfolioAssessments.AsSplitQuery()
            .Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets)
            .Include(x => x.InvestmentEvent!).ThenInclude(x => x.Evidence)
            .Include(x => x.InvestmentEvent!).ThenInclude(x => x.DocumentAnalysis!).ThenInclude(x => x.Document)
            .Where(x => x.PortfolioId == portfolioId && analysisIds.Contains(x.InvestmentEvent!.DocumentAnalysisId))
            .ToListAsync(ct);
        if (!related.Any(x => x.Id == assessment.Id)) related.Add(assessment);
        return related;
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

    private static RadarItemDto ToDto(PortfolioAssessment x, IReadOnlyList<RadarEventDto> events, IEnumerable<PortfolioAssessment>? related = null)
    {
        var attention = RadarAttentionNowPolicy.RequiresAttention(x);
        var eventItem = x.InvestmentEvent!;
        var evidence = (related ?? [x]).OrderByDescending(a => a.Id == x.Id)
            .SelectMany(a => a.InvestmentEvent!.Evidence.Select(e => new RadarEvidenceDto(e.DocumentId, e.DocumentAnalysisId, e.Reference)))
            .Distinct().ToList();
        var references = evidence.Select(e => new EvidenceReference("Document", e.DocumentId.ToString(), e.Reference, e.Reference, eventItem.OccurredAt)).ToList();
        return new(x.Id, eventItem.Id, eventItem.DocumentAnalysisId, eventItem.AffectedAssets.Select(a => a.Ticker).ToList(), eventItem.EventType, eventItem.OccurredAt, x.AttentionPriority, x.ActionRequirement, x.CapitalDecisionImpact, x.Status, attention, eventItem.Summary, x.Summary, x.WhyItMatters, x.SuggestedBehavior, x.Confidence, x.GeneratedAt, evidence, FreshnessStatus.Fresh, Array.Empty<FreshnessReason>(), references, RadarEventPolicy.Importance(eventItem).ToString(), events, eventItem.DocumentAnalysis?.Document?.PublishedAt, eventItem.DocumentAnalysis?.Impact ?? "", eventItem.RadarDetails?.Novelty ?? "Unknown", eventItem.RadarDetails?.ImpactDirection ?? "Indeterminate", eventItem.RadarDetails?.Materiality ?? "Unknown");
    }
}

public sealed record SetRadarStatusRequest(string Status);
public sealed record RadarResponse(IReadOnlyList<RadarItemDto> AttentionNow, IReadOnlyList<RadarItemDto> RecentUpdates, int RequiresAttention, int Page = 1, int PageSize = 10, int TotalItems = 0, int TotalPages = 0);
public sealed record RadarRepairResponse(int QueuedCount);
public sealed record RadarBulkUpdateResponse(int UpdatedCount);
public sealed record RadarEvidenceDto(Guid DocumentId, Guid DocumentAnalysisId, string Reference);
public sealed record RadarEventDto(Guid InvestmentEventId, InvestmentEventType EventType, DateTimeOffset? OccurredAt, string Summary);
public sealed record RadarItemDto(Guid AssessmentId, Guid InvestmentEventId, Guid DocumentAnalysisId, IReadOnlyList<string> AffectedAssets, InvestmentEventType EventType, DateTimeOffset? OccurredAt, AttentionPriority AttentionPriority, ActionRequirement ActionRequirement, CapitalDecisionImpact CapitalDecisionImpact, AssessmentStatus Status, bool RequiresAttention, string EventSummary, string Summary, string WhyItMatters, string SuggestedBehavior, IntelligenceConfidence Confidence, DateTimeOffset GeneratedAt, IReadOnlyList<RadarEvidenceDto> Evidence, FreshnessStatus Freshness = FreshnessStatus.Fresh, IReadOnlyList<FreshnessReason>? FreshnessReasons = null, IReadOnlyList<EvidenceReference>? EvidenceReferences = null, string DocumentImportance = "Unknown", IReadOnlyList<RadarEventDto>? Events = null, DateTimeOffset? DocumentPublishedAt = null, string Impact = "", string Novelty = "Unknown", string ImpactDirection = "Indeterminate", string Materiality = "Unknown");
internal sealed record RadarGroup(PortfolioAssessment Representative, IReadOnlyList<RadarEventDto> Events)
{
    public AttentionPriority AttentionPriority => Representative.AttentionPriority;
    public AssessmentStatus Status => Representative.Status;
}
