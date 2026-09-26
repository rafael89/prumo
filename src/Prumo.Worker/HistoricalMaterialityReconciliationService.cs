using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prumo.Application.Abstractions;
using Prumo.Application.Documents;
using Prumo.Application.Portfolio;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Worker;

public sealed record HistoricalMaterialityReconciliationReport(
    int Inspected, int Eligible, int Changed, int RelevantToInformative,
    int CriticalToOther, int Unchanged, int SkippedNotReconcilable, int Failures);

/// <summary>Reconciles persisted analyses using deterministic rules only; it never invokes AI or document storage.</summary>
public sealed class HistoricalMaterialityReconciliationService
{
    private readonly PrumoDbContext db;
    private readonly IPortfolioImpactAssessmentService impactService;
    private readonly ILogger<HistoricalMaterialityReconciliationService> logger;

    public HistoricalMaterialityReconciliationService(PrumoDbContext db, IPortfolioImpactAssessmentService impactService, ILogger<HistoricalMaterialityReconciliationService> logger)
    {
        this.db = db; this.impactService = impactService; this.logger = logger;
    }

    public async Task<HistoricalMaterialityReconciliationReport> ReconcileAsync(CancellationToken ct)
    {
        logger.LogInformation("Historical materiality reconciliation started.");
        // Use an explicit FK join: processed historical analyses are selected
        // by persisted status, independently of navigation fix-up.
        var analyses = await (from analysis in db.DocumentAnalyses
                              join document in db.Documents on analysis.DocumentId equals document.Id
                              where document.ProcessingStatus == DocumentProcessingStatus.Processed
                              select analysis)
            .Include(x => x.Document).ThenInclude(x => x!.Asset)
            .OrderBy(x => x.Id).ToListAsync(ct);
        var analysisIds = analyses.Select(x => x.Id).ToList();
        var events = await db.InvestmentEvents.Include(x => x.AffectedAssets)
            .Where(x => analysisIds.Contains(x.DocumentAnalysisId)).ToListAsync(ct);
        var eventsByAnalysis = events.GroupBy(x => x.DocumentAnalysisId).ToDictionary(x => x.Key, x => x.ToList());
        logger.LogInformation("Historical reconciliation found {AnalysisCount} processed analyses and {EventCount} linked events.", analyses.Count, events.Count);

        var inspected = 0; var eligible = 0; var changed = 0; var relevantToInformative = 0;
        var criticalToOther = 0; var unchanged = 0; var skipped = 0; var failures = 0;
        await using IDbContextTransaction? transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;

        foreach (var analysis in analyses)
        {
            inspected++;
            var result = DocumentMaterialityNormalization.Normalize(
                analysis.Importance, analysis.Classification, analysis.RequiresAction, analysis.SuggestedAction,
                analysis.Summary, analysis.Impact,
                eventsByAnalysis.GetValueOrDefault(analysis.Id, new()).Select(x => new MaterialityEventFacts(x.Summary, x.Facts)));
            if (!result.Reconcilable) { skipped++; continue; }
            eligible++;
            logger.LogDebug("Historical analysis {AnalysisId}: eligible=true, changed={Changed}, before={Importance}/{Classification}/{RequiresAction}/{SuggestedAction}, after={NewImportance}/{NewRequiresAction}/{NewSuggestedAction}.", analysis.Id, result.Changed, analysis.Importance, analysis.Classification, analysis.RequiresAction, analysis.SuggestedAction, result.Importance, result.RequiresAction, result.SuggestedAction);
            if (!result.Changed) { unchanged++; continue; }

            var oldImportance = analysis.Importance;
            analysis.ApplyDeterministicMateriality(result.Importance, result.RequiresAction, result.SuggestedAction);
            changed++;
            if (oldImportance == AnalysisImportance.Relevant && result.Importance == AnalysisImportance.Informative) relevantToInformative++;
            else if (oldImportance == AnalysisImportance.Critical && result.Importance != AnalysisImportance.Critical) criticalToOther++;
            foreach (var investmentEvent in eventsByAnalysis.GetValueOrDefault(analysis.Id, new()))
                await RecalculateAssessmentsAsync(analysis, investmentEvent, ct);
        }

        if (changed > 0)
        {
            logger.LogInformation("Saving {ChangedCount} materiality changes; tracked entities={TrackedCount}.", changed, db.ChangeTracker.Entries().Count());
            var saved = await db.SaveChangesAsync(ct);
            logger.LogInformation("SaveChangesAsync completed with {SavedCount} database changes.", saved);
            if (transaction is not null)
            {
                await transaction.CommitAsync(ct);
                logger.LogInformation("Historical reconciliation transaction committed.");
            }
        }

        var report = new HistoricalMaterialityReconciliationReport(inspected, eligible, changed, relevantToInformative, criticalToOther, unchanged, skipped, failures);
        logger.LogInformation("Historical materiality reconciliation completed: Inspected={Inspected} Eligible={Eligible} Changed={Changed} RelevantToInformative={RelevantToInformative} CriticalToOther={CriticalToOther} Unchanged={Unchanged} SkippedNotReconcilable={SkippedNotReconcilable} Failures={Failures}", report.Inspected, report.Eligible, report.Changed, report.RelevantToInformative, report.CriticalToOther, report.Unchanged, report.SkippedNotReconcilable, report.Failures);
        return report;
    }

    private async Task RecalculateAssessmentsAsync(DocumentAnalysis analysis, InvestmentEvent investmentEvent, CancellationToken ct)
    {
        var assetIds = investmentEvent.AffectedAssets.Select(x => x.Id).ToHashSet();
        if (assetIds.Count == 0) return;
        var portfolios = await db.Portfolios.Include(x => x.Positions).ThenInclude(x => x.Asset).ToListAsync(ct);
        var targets = await db.TargetAllocations.ToListAsync(ct);
        foreach (var portfolio in portfolios.Where(x => x.Positions.Any(p => assetIds.Contains(p.AssetId) && p.Quantity > 0)).OrderBy(x => x.Id))
        {
            var portfolioTargets = targets.Where(x => x.PortfolioId == portfolio.Id).ToList();
            var targetKey = TargetAllocationKeyCalculator.Calculate(portfolioTargets);
            var relevant = portfolio.Positions.Where(p => assetIds.Contains(p.AssetId) && p.Quantity > 0)
                .Select(p => new RelevantPositionContext(p.AssetId, p.Asset?.Ticker ?? string.Empty, p.Asset?.AssetClass ?? AssetClass.Other, p.CurrentValue ?? 0, p.PortfolioWeight)).ToList();
            var health = targetKey is null ? null : new PortfolioHealthCalculator().Calculate(portfolio, portfolioTargets);
            var output = impactService.AssessPortfolioImpact(new PortfolioImpactAssessmentContext(investmentEvent, portfolio, relevant, health, analysis.Importance, analysis.RequiresAction, analysis.SuggestedAction));
            var snapshot = PortfolioSnapshotKeyCalculator.Calculate(portfolio);
            var current = await db.PortfolioAssessments.FirstOrDefaultAsync(x => x.PortfolioId == portfolio.Id && x.InvestmentEventId == investmentEvent.Id && x.PortfolioSnapshotKey == snapshot && x.TargetAllocationKey == targetKey && x.AssessmentVersion == impactService.Version, ct);
            if (current is not null)
            {
                current.ApplyRecalculatedValues(output.AttentionPriority, output.ActionRequirement, output.CapitalDecisionImpact, output.Summary, output.WhyItMatters, output.SuggestedBehavior, output.Confidence, investmentEvent.Version);
                continue;
            }
            var previous = await db.PortfolioAssessments.Where(x => x.PortfolioId == portfolio.Id && x.InvestmentEventId == investmentEvent.Id).OrderByDescending(x => x.GeneratedAt).FirstOrDefaultAsync(ct);
            var replacement = new PortfolioAssessment(portfolio.Id, investmentEvent.Id, output.AttentionPriority, output.ActionRequirement, output.CapitalDecisionImpact, previous?.Status ?? AssessmentStatus.Detected, previous?.UserState ?? AssessmentUserState.Unread, output.Summary, output.WhyItMatters, output.SuggestedBehavior, output.Confidence, targetKey, impactService.Version, snapshot, investmentEvent.Version);
            replacement.AttachInvestmentEvent(investmentEvent);
            db.PortfolioAssessments.Add(replacement);
        }
    }
}

public sealed class HistoricalMaterialityReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<HistoricalMaterialityReconciliationWorker> logger;
    public HistoricalMaterialityReconciliationWorker(IServiceScopeFactory scopeFactory, ILogger<HistoricalMaterialityReconciliationWorker> logger) { this.scopeFactory = scopeFactory; this.logger = logger; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("HistoricalMaterialityReconciliationWorker invoked during startup.");
        for (var attempt = 1; attempt <= 10 && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<HistoricalMaterialityReconciliationService>().ReconcileAsync(stoppingToken);
                logger.LogInformation("HistoricalMaterialityReconciliationWorker completed successfully during startup.");
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) when (attempt < 10)
            {
                logger.LogWarning(exception, "Historical reconciliation startup attempt {Attempt}/10 failed; retrying in 2s.", attempt);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (Exception exception) { logger.LogError(exception, "Historical reconciliation failed definitively during startup."); }
        }
    }
}
