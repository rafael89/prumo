using Prumo.Application.Abstractions;
using Prumo.Application.Portfolio;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Prumo.Api.Services;

public sealed class PortfolioAssessmentCurrentService
{
    private readonly PrumoDbContext db;
    private readonly IPortfolioImpactAssessmentService impactService;
    private readonly IPortfolioAssessmentFreshnessEvaluator freshnessEvaluator;
    private readonly ILogger<PortfolioAssessmentCurrentService> logger;

    public PortfolioAssessmentCurrentService(
        PrumoDbContext db,
        IPortfolioImpactAssessmentService impactService,
        IPortfolioAssessmentFreshnessEvaluator freshnessEvaluator,
        ILogger<PortfolioAssessmentCurrentService> logger)
    {
        this.db = db;
        this.impactService = impactService;
        this.freshnessEvaluator = freshnessEvaluator;
        this.logger = logger;
    }

    public async Task<IReadOnlyList<PortfolioAssessment>> EnsureFreshAsync(
        Portfolio portfolio,
        IReadOnlyList<TargetAllocation> targets,
        IReadOnlyList<PortfolioAssessment> assessments,
        CancellationToken ct)
    {
        var snapshot = PortfolioSnapshotKeyCalculator.Calculate(portfolio);
        var targetKey = TargetAllocationKeyCalculator.Calculate(targets);
        var current = new List<PortfolioAssessment>();
        var keys = new HashSet<string>();
        var pending = new List<PortfolioAssessment>();
        // A portfolio change creates a new assessment version, but it must not
        // turn an already handled document into a new Radar notification. The
        // Radar state is document-oriented: duplicate event assessments for
        // one document must share the strongest state found in the history.
        // This is important when one old assessment is Seen and another stale
        // duplicate is still Unread.
        var stateByDocument = assessments
            .Where(x => x.InvestmentEvent is not null)
            .GroupBy(x => x.InvestmentEvent!.DocumentAnalysisId)
            .ToDictionary(
                group => group.Key,
                group => new DocumentRadarState(
                    group.Any(x => x.Status == AssessmentStatus.Closed),
                    group.Any(x => x.UserState == AssessmentUserState.Seen)));

        // Keep the acquisition order stable across API requests and the worker.
        foreach (var assessment in assessments
                     .Where(x => x.Status != AssessmentStatus.Closed)
                     .OrderBy(x => x.InvestmentEventId)
                     .ThenBy(x => x.Id))
        {
            var freshness = freshnessEvaluator.Evaluate(assessment, snapshot, targetKey, assessment.InvestmentEvent, impactService.Version);
            logger.LogInformation(
                "PortfolioAssessment freshness evaluated PortfolioId={PortfolioId} AssessmentId={AssessmentId} FreshnessStatus={FreshnessStatus}",
                portfolio.Id, assessment.Id, freshness.Status);

            var key = $"{assessment.InvestmentEventId}|{snapshot}|{targetKey}|{impactService.Version}";
            if (freshness.Status == FreshnessStatus.Fresh)
            {
                var affectedAssetIds = assessment.InvestmentEvent?.AffectedAssets.Select(x => x.Id).ToHashSet() ?? new HashSet<Guid>();
                var hasAffectedPosition = portfolio.Positions.Any(x => affectedAssetIds.Contains(x.AssetId));
                if (assessment.InvestmentEvent?.EventType == InvestmentEventType.Liquidation && hasAffectedPosition)
                    assessment.OverrideCapitalDecisionImpact(CapitalDecisionImpact.Block);
                current.Add(assessment);
                keys.Add(key);
                continue;
            }

            if (assessment.InvestmentEvent is null || !keys.Add(key)) continue;

            var affected = assessment.InvestmentEvent.AffectedAssets.Select(x => x.Id).ToHashSet();
            var relevant = portfolio.Positions
                .Where(x => affected.Contains(x.AssetId))
                .Select(x => new RelevantPositionContext(
                    x.AssetId,
                    x.Asset?.Ticker ?? string.Empty,
                    x.Asset?.AssetClass ?? AssetClass.Other,
                    x.CurrentValue ?? 0,
                    x.PortfolioWeight))
                .ToList();
            var health = targetKey is null ? null : new PortfolioHealthCalculator().Calculate(portfolio, targets);
            var analysis = assessment.InvestmentEvent.DocumentAnalysis;
            var output = impactService.AssessPortfolioImpact(new PortfolioImpactAssessmentContext(assessment.InvestmentEvent, portfolio, relevant, health, analysis?.Importance, analysis?.RequiresAction, analysis?.SuggestedAction));
            var previousState = stateByDocument.GetValueOrDefault(assessment.InvestmentEvent.DocumentAnalysisId);
            var replacement = new PortfolioAssessment(
                portfolio.Id,
                assessment.InvestmentEventId,
                output.AttentionPriority,
                output.ActionRequirement,
                output.CapitalDecisionImpact,
                // Recalculating the portfolio impact must not turn an old Radar
                // item into a new notification. Preserve the user state from the
                // current assessment; a closed assessment still takes precedence
                // so a target/portfolio change cannot reopen it.
                previousState?.IsClosed == true ? AssessmentStatus.Closed : assessment.Status,
                previousState?.WasSeen == true ? AssessmentUserState.Seen : assessment.UserState,
                output.Summary,
                output.WhyItMatters,
                output.SuggestedBehavior,
                output.Confidence,
                targetKey,
                impactService.Version,
                snapshot,
                assessment.InvestmentEvent.Version);
            replacement.AttachInvestmentEvent(assessment.InvestmentEvent);
            db.Entry(replacement).State = EntityState.Added;
            pending.Add(replacement);
            current.Add(replacement);
        }

        if (pending.Count == 0) return current;

        var eventIds = pending.Select(x => x.InvestmentEventId).Distinct().ToArray();
        var persisted = await db.PortfolioAssessments
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets)
            .Include(x => x.InvestmentEvent!).ThenInclude(x => x.Evidence)
            .Include(x => x.InvestmentEvent!).ThenInclude(x => x.DocumentAnalysis!).ThenInclude(x => x.Document!)
            .Where(x => x.PortfolioId == portfolio.Id
                && eventIds.Contains(x.InvestmentEventId)
                && x.PortfolioSnapshotKey == snapshot
                && x.TargetAllocationKey == targetKey
                && x.AssessmentVersion == impactService.Version)
            .ToListAsync(ct);

        if (persisted.Count > 0)
        {
            var persistedByEvent = persisted.ToDictionary(x => x.InvestmentEventId);
            foreach (var replacement in pending.Where(x => persistedByEvent.ContainsKey(x.InvestmentEventId)).ToList())
            {
                db.Entry(replacement).State = EntityState.Detached;
                current.Remove(replacement);
            }

            current.AddRange(persisted.Where(item => current.All(existing => existing.Id != item.Id)));
            pending.RemoveAll(item => persistedByEvent.ContainsKey(item.InvestmentEventId));
        }

        if (pending.Count == 0) return current;

        try
        {
            await PostgresTransientFailure.SaveChangesWithDeadlockRetryAsync(db, ct);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            // Another request may have created the same current assessment between
            // our freshness read and this insert. The unique index is the authority;
            // discard our transient entities and read the winner back.
            foreach (var replacement in pending) db.Entry(replacement).State = EntityState.Detached;
            current.RemoveAll(pending.Contains);

            var recovered = await db.PortfolioAssessments
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets)
                .Include(x => x.InvestmentEvent!).ThenInclude(x => x.Evidence)
                .Include(x => x.InvestmentEvent!).ThenInclude(x => x.DocumentAnalysis!).ThenInclude(x => x.Document!)
                .Where(x => x.PortfolioId == portfolio.Id
                    && eventIds.Contains(x.InvestmentEventId)
                    && x.PortfolioSnapshotKey == snapshot
                    && x.TargetAllocationKey == targetKey
                    && x.AssessmentVersion == impactService.Version)
                .ToListAsync(ct);
            current.AddRange(recovered.Where(item => current.All(existing => existing.Id != item.Id)));
            logger.LogInformation("PortfolioAssessment reavaliação concorrente resolvida PortfolioId={PortfolioId} RecoveredCount={RecoveredCount}", portfolio.Id, recovered.Count);
        }

        return current;
    }

    private sealed record DocumentRadarState(bool IsClosed, bool WasSeen);

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
        => exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
