using Microsoft.EntityFrameworkCore;
using Prumo.Application.Documents;
using Prumo.Application.Abstractions;
using Prumo.Application.Intelligence;
using Prumo.Application.Portfolio;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Worker;

public sealed record HistoricalMaterialityReconciliationReport(int Inspected, int Changed, int RelevantToInformative, int CriticalToOther, int Unchanged, int SkippedNotReconcilable, int Failures);

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
        var analyses = await db.DocumentAnalyses
            .Include(x => x.Document).ThenInclude(x => x!.Asset)
            .Where(x => x.Document!.ProcessingStatus == DocumentProcessingStatus.Processed)
            .OrderBy(x => x.Id).ToListAsync(ct);
        var events = await db.InvestmentEvents.Include(x => x.AffectedAssets)
            .Where(x => analyses.Select(a => a.Id).Contains(x.DocumentAnalysisId)).ToListAsync(ct);
        var eventsByAnalysis = events.GroupBy(x => x.DocumentAnalysisId).ToDictionary(x => x.Key, x => x.ToList());
        var inspected = 0; var changed = 0; var relevantToInformative = 0; var criticalToOther = 0; var unchanged = 0; var skipped = 0; var failures = 0;

        foreach (var analysis in analyses)
        {
            inspected++;
            var result = DocumentMaterialityNormalization.Normalize(
                analysis.Importance, analysis.Classification, analysis.RequiresAction, analysis.SuggestedAction,
                analysis.Summary, analysis.Impact,
                eventsByAnalysis.GetValueOrDefault(analysis.Id, new()).Select(x => new MaterialityEventFacts(x.Summary, x.Facts)));
            if (!result.Reconcilable) { skipped++; continue; }
            if (!result.Changed) { unchanged++; continue; }

            var oldImportance = analysis.Importance;
            analysis.ApplyDeterministicMateriality(result.Importance, result.RequiresAction, result.SuggestedAction);
            changed++;
            if (oldImportance == AnalysisImportance.Relevant && result.Importance == AnalysisImportance.Informative) relevantToInformative++;
            else if (oldImportance == AnalysisImportance.Critical && result.Importance != AnalysisImportance.Critical) criticalToOther++;

            foreach (var investmentEvent in eventsByAnalysis.GetValueOrDefault(analysis.Id, new()))
                await RecalculateAssessmentsAsync(analysis, investmentEvent, ct);
        }

        if (changed > 0) await db.SaveChangesAsync(ct);
        var report = new HistoricalMaterialityReconciliationReport(inspected, changed, relevantToInformative, criticalToOther, unchanged, skipped, failures);
        logger.LogInformation("Reconciliação histórica de materialidade concluída: Inspected={Inspected} Changed={Changed} RelevantToInformative={RelevantToInformative} CriticalToOther={CriticalToOther} Unchanged={Unchanged} SkippedNotReconcilable={SkippedNotReconcilable} Failures={Failures}", report.Inspected, report.Changed, report.RelevantToInformative, report.CriticalToOther, report.Unchanged, report.SkippedNotReconcilable, report.Failures);
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
            var replacement = new PortfolioAssessment(portfolio.Id, investmentEvent.Id, output.AttentionPriority, output.ActionRequirement, output.CapitalDecisionImpact,
                previous?.Status ?? AssessmentStatus.Detected, previous?.UserState ?? AssessmentUserState.Unread, output.Summary, output.WhyItMatters, output.SuggestedBehavior, output.Confidence, targetKey, impactService.Version, snapshot, investmentEvent.Version);
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
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<HistoricalMaterialityReconciliationService>().ReconcileAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogError(exception, "Falha na reconciliação histórica de materialidade."); }
    }
}
