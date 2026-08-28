using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prumo.Api.Controllers;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Domain.Tests;

public sealed class DocumentsControllerTests
{
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
        await dbContext.SaveChangesAsync();

        var result = await new DocumentsController(dbContext).GetLatestAnalysis(document.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<DocumentAnalysisResponse>(ok.Value);
        Assert.Equal("gemini-3.5-flash-lite", response.Model);
        Assert.Equal(latestAnalysis.Id, response.Id);
        Assert.Equal(2, await dbContext.DocumentAnalyses.CountAsync(analysis => analysis.DocumentId == document.Id));
    }

    private static DocumentAnalysis CreateAnalysis(Guid documentId, string model) => new(
        Guid.NewGuid(), documentId, model, "document-analysis-v1", AnalysisImportance.Informative,
        AnalysisClassification.Other, false, SuggestedAction.NoAction, "Resumo", "Impacto",
        "Impacto na carteira", new[] { "XPLG11" }, 1m, "{\"source\":\"test\"}");
}
