using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prumo.Api.Controllers;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Domain.Rules;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Domain.Tests;

public sealed class DocumentsControllerTests
{
    [Fact]
    public async Task Reprocess_failed_download_requeues_it_and_resets_attempts()
    {
        var options = new DbContextOptionsBuilder<PrumoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new PrumoDbContext(options);
        var asset = new Asset("TEST3", "Teste", AssetType.Stock, "Teste");
        var portfolio = new Portfolio("Carteira");
        portfolio.Positions.Add(new PortfolioPosition(portfolio.Id, asset.Id, 1m, 100m, 100m));
        var document = new Document(asset.Id, DocumentType.Other, DateTimeOffset.UtcNow, "CVM", "doc", "https://example.test/doc", "fingerprint");
        var job = new ProcessingJob(document.Id, ProcessingJobType.DownloadDocument, DateTimeOffset.UtcNow);
        job.Start(DateTimeOffset.UtcNow);
        job.MarkFailedPermanently(DateTimeOffset.UtcNow, "Download inválido");
        document.MarkError();
        db.AddRange(asset, portfolio, document, job);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DocumentsController(db).ReprocessFailedDocuments(portfolio.Id, null, TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.IsType<ReprocessFailedDocumentsResponse>(Assert.IsType<OkObjectResult>(result.Result).Value).RequeuedCount);
        Assert.Equal(ProcessingJobStatus.Queued, job.Status);
        Assert.Equal(0, job.Attempts);
        Assert.Equal(DocumentProcessingStatus.Discovered, document.ProcessingStatus);
    }
    [Fact]
    public async Task Duplicate_publication_returns_original_analysis_without_copying_it()
    {
        var options = new DbContextOptionsBuilder<PrumoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new PrumoDbContext(options);
        var asset = new Asset("XPLG11", "XP Log", AssetType.Fii, "Logística");
        var original = new Document(asset.Id, DocumentType.RelevantFact, DateTimeOffset.UtcNow,
            "CVM", "original", "https://example.test/original", "original");
        var duplicate = new Document(asset.Id, DocumentType.Other, DateTimeOffset.UtcNow,
            "CVM", "duplicate", "https://example.test/duplicate", "duplicate");
        duplicate.MarkDuplicate(original);
        var analysis = CreateAnalysis(original.Id, "fake-model");
        db.AddRange(asset, original, duplicate, analysis);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        var controller = new DocumentsController(db);

        var result = await controller.GetLatestAnalysis(duplicate.Id, TestContext.Current.CancellationToken);
        var response = Assert.IsType<DocumentAnalysisResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(analysis.Id, response.Id);
        Assert.Equal(original.Id, response.DocumentId);
        Assert.Single(db.DocumentAnalyses);
        var detail = await controller.GetDocument(duplicate.Id, TestContext.Current.CancellationToken);
        Assert.Equal(original.Id, Assert.IsType<DocumentResponse>(Assert.IsType<OkObjectResult>(detail.Result).Value).DuplicateOfDocumentId);
    }

    [Fact]
    public async Task RetornaAnaliseMaisRecenteDoMesmoDocumentoIndependentementeDoModelo()
    {
        var options = new DbContextOptionsBuilder<PrumoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new PrumoDbContext(options);
        var asset = new Asset("XPLG11", "XP Log", AssetType.Fii, "Logística");
        var document = new Document(asset.Id, DocumentType.RelevantFact, DateTimeOffset.UtcNow, "CVM", "doc-1", "https://example.test/doc-1", "fingerprint-1");
        dbContext.Assets.Add(asset);
        dbContext.Documents.Add(document);

        var oldAnalysis = CreateAnalysis(document.Id, "fake-e2e");
        var latestAnalysis = CreateAnalysis(document.Id, "gemini-3.5-flash-lite");
        dbContext.DocumentAnalyses.AddRange(oldAnalysis, latestAnalysis);
        dbContext.Entry(oldAnalysis).Property(analysis => analysis.CreatedAt).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-2);
        dbContext.Entry(latestAnalysis).Property(analysis => analysis.CreatedAt).CurrentValue = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DocumentsController(dbContext).GetLatestAnalysis(document.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<DocumentAnalysisResponse>(ok.Value);
        Assert.Equal("gemini-3.5-flash-lite", response.Model);
        Assert.Equal(latestAnalysis.Id, response.Id);
        Assert.Equal(2, await dbContext.DocumentAnalyses.CountAsync(analysis => analysis.DocumentId == document.Id, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Document_detail_refreshes_stale_portfolio_attention()
    {
        var options = new DbContextOptionsBuilder<PrumoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrumoDbContext(options);
        var asset = new Asset("TRXF11", "TRX", AssetType.Fii, "Logística");
        var portfolio = new Portfolio("Carteira");
        portfolio.Positions.Add(new PortfolioPosition(portfolio.Id, asset.Id, 1m, 100m, 100m));
        PortfolioCalculator.RecalculateWeights(portfolio.Positions);
        var document = new Document(asset.Id, DocumentType.RelevantFact, DateTimeOffset.UtcNow,
            "CVM", "doc-2", "https://example.test/doc-2", "fingerprint-2");
        var analysis = new DocumentAnalysis(Guid.NewGuid(), document.Id, "test", "test-v1",
            AnalysisImportance.Relevant, AnalysisClassification.AssetTransaction, false, SuggestedAction.Maintain,
            "Aquisição", "Impacto", "Impacto", [asset.Ticker], 1m, "{}");
        var investmentEvent = new InvestmentEvent(analysis.Id, InvestmentEventType.AssetTransaction,
            document.PublishedAt, "Aquisição", []);
        investmentEvent.AffectedAssets.Add(asset);
        var stale = new PortfolioAssessment(portfolio.Id, investmentEvent.Id, AttentionPriority.Critical,
            ActionRequirement.Review, CapitalDecisionImpact.None, AssessmentStatus.Detected,
            AssessmentUserState.Unread, "Resumo", "Motivo", "Revisar", IntelligenceConfidence.High,
            null, "portfolio-impact-v6", "snapshot-antigo", investmentEvent.Version);
        db.AddRange(asset, portfolio, document, analysis, investmentEvent, stale);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new DocumentsController(db).GetLatestAnalysis(document.Id, portfolio.Id, CancellationToken.None);

        var response = Assert.IsType<DocumentAnalysisResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.NotNull(response.PortfolioAssessment);
        Assert.False(response.PortfolioAssessment.RequiresAttention);
        Assert.Equal("None", response.PortfolioAssessment.ActionRequirement);
    }

    private static DocumentAnalysis CreateAnalysis(Guid documentId, string model) => new(
        Guid.NewGuid(), documentId, model, "document-analysis-v1", AnalysisImportance.Informative,
        AnalysisClassification.Other, false, SuggestedAction.NoAction, "Resumo", "Impacto",
        "Impacto na carteira", new[] { "XPLG11" }, 1m, "{\"source\":\"test\"}");
}
