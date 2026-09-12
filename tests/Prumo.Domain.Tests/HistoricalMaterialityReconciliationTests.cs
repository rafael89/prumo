using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prumo.Application.Intelligence;
using Prumo.Application.Portfolio;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Worker;

namespace Prumo.Domain.Tests;

public sealed class HistoricalMaterialityReconciliationTests
{
    [Fact]
    public async Task Reconciliation_persists_historical_reclassification_and_is_idempotent()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await using (var seed = CreateDb(databaseName))
        {
            var asset = new Asset("TEST11", "Ativo teste", AssetType.Fii, "Teste");
            var portfolio = new Portfolio("Carteira teste");
            portfolio.Positions.Add(new PortfolioPosition(portfolio.Id, asset.Id, 10m, 100m, 6m));
            var document = new Document(asset.Id, DocumentType.Other, DateTimeOffset.UtcNow.AddYears(-2), "historico", "historico-1", "https://example.test/historico", Guid.NewGuid().ToString("N"));
            document.MarkProcessed();
            var analysis = new DocumentAnalysis(Guid.NewGuid(), document.Id, "legacy-model", "legacy-prompt-v1", AnalysisImportance.Relevant, AnalysisClassification.Distribution, false, SuggestedAction.NoAction, "Distribuição recorrente", "Pagamento regular dentro do padrão", "Impacto", new[] { asset.Ticker }, 1m, "{}");
            var investmentEvent = new InvestmentEvent(analysis.Id, InvestmentEventType.Distribution, document.PublishedAt, "Distribuição recorrente", Array.Empty<string>());
            investmentEvent.AffectedAssets.Add(asset);
            seed.AddRange(asset, portfolio, document, analysis, investmentEvent);
            await seed.SaveChangesAsync();

            var snapshot = PortfolioSnapshotKeyCalculator.Calculate(portfolio);
            var assessment = new PortfolioAssessment(portfolio.Id, investmentEvent.Id, AttentionPriority.High, ActionRequirement.Review, CapitalDecisionImpact.Caution, AssessmentStatus.Detected, AssessmentUserState.Unread, "avaliação antiga", "motivo antigo", "ação antiga", IntelligenceConfidence.High, null, PortfolioImpactAssessmentService.CurrentVersion, snapshot, investmentEvent.Version);
            assessment.AttachInvestmentEvent(investmentEvent);
            seed.PortfolioAssessments.Add(assessment);
            await seed.SaveChangesAsync();
            Assert.Equal(1, await seed.Documents.CountAsync());
            Assert.Equal(DocumentProcessingStatus.Processed, (await seed.Documents.SingleAsync()).ProcessingStatus);
        }

        HistoricalMaterialityReconciliationReport firstReport;
        await using (var run = CreateDb(databaseName))
        {
            Assert.Equal(1, await run.Documents.CountAsync());
            Assert.Equal(DocumentProcessingStatus.Processed, (await run.Documents.SingleAsync()).ProcessingStatus);
            Assert.Equal(1, await run.Documents.Where(x => x.ProcessingStatus == DocumentProcessingStatus.Processed).CountAsync());
            Assert.Equal((await run.Documents.SingleAsync()).Id, (await run.DocumentAnalyses.SingleAsync()).DocumentId);
            firstReport = await Service(run).ReconcileAsync(CancellationToken.None);
        }

        Assert.Equal(1, firstReport.Inspected);
        Assert.Equal(1, firstReport.Eligible);
        Assert.Equal(1, firstReport.Changed);

        await using (var verify = CreateDb(databaseName))
        {
            var persisted = await verify.DocumentAnalyses.AsNoTracking().SingleAsync();
            Assert.Equal(AnalysisImportance.Informative, persisted.Importance);
            Assert.Equal(1, await verify.DocumentAnalyses.CountAsync());
            Assert.Equal(1, await verify.InvestmentEvents.CountAsync());
            var assessment = await verify.PortfolioAssessments.AsNoTracking().SingleAsync();
            Assert.Equal(ActionRequirement.None, assessment.ActionRequirement);
            Assert.Equal(CapitalDecisionImpact.None, assessment.CapitalDecisionImpact);
            Assert.Equal(AttentionPriority.Low, assessment.AttentionPriority);
        }

        Assert.Equal(1, firstReport.Inspected);
        Assert.Equal(1, firstReport.Eligible);
        Assert.Equal(1, firstReport.Changed);
        Assert.Equal(1, firstReport.RelevantToInformative);

        await using (var secondRun = CreateDb(databaseName))
        {
            var secondReport = await Service(secondRun).ReconcileAsync(CancellationToken.None);
            Assert.Equal(0, secondReport.Changed);
            Assert.Equal(0, secondReport.RelevantToInformative);
        }
    }

    private static HistoricalMaterialityReconciliationService Service(PrumoDbContext db)
        => new(db, new PortfolioImpactAssessmentService(), NullLogger<HistoricalMaterialityReconciliationService>.Instance);

    private static PrumoDbContext CreateDb(string name)
        => new(new DbContextOptionsBuilder<PrumoDbContext>().UseInMemoryDatabase(name).Options);
}
