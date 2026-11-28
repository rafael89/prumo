using Prumo.Api.Controllers;
using Prumo.Api.Services;
using Prumo.Application.Abstractions;
using Prumo.Application.Contributions;
using Prumo.Application.Intelligence;
using Prumo.Application.Portfolio;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.MarketData;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace Prumo.Domain.Tests;

public sealed class ContributionGuidanceControllerTests
{
    [Fact]
    public async Task GenerateDoesNotReuseGuidanceFromPreviousEngineVersion()
    {
        await using var db = CreateDbContext();
        var (portfolio, targets) = await SeedPortfolioAsync(db);
        var oldGuidance = AddCompletedGuidance(db, portfolio, targets, "R4-2.0");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateController(db).Generate(portfolio.Id, new ContributionRequest(4_000m), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result.Result);
        var guidances = await db.ContributionGuidances.OrderBy(x => x.GeneratedAt).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, guidances.Count);
        Assert.Equal(oldGuidance.Id, guidances[0].Id);
        Assert.Equal("R4-9.2", guidances[1].EngineVersion);
        Assert.Equal("pending-ai", guidances[1].Provider);
        Assert.Equal(ContributionGuidanceJobStatus.Queued, await db.ContributionGuidanceJobs
            .Where(x => x.ContributionGuidanceId == guidances[1].Id)
            .Select(x => x.Status)
            .SingleAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GenerateCreatesNewGuidanceEvenWhenCurrentVersionIsCompleted()
    {
        await using var db = CreateDbContext();
        var (portfolio, targets) = await SeedPortfolioAsync(db);
        var currentGuidance = AddCompletedGuidance(db, portfolio, targets, "R4-9.2");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateController(db).Generate(portfolio.Id, new ContributionRequest(4_000m), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result.Result);
        var guidances = await db.ContributionGuidances.OrderBy(x => x.GeneratedAt).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, guidances.Count);
        Assert.Equal(currentGuidance.Id, guidances[0].Id);
        Assert.Equal("R4-9.2", guidances[1].EngineVersion);
        Assert.Equal("pending-ai", guidances[1].Provider);
        Assert.Equal(ContributionGuidanceJobStatus.Queued, await db.ContributionGuidanceJobs
            .Where(x => x.ContributionGuidanceId == guidances[1].Id)
            .Select(x => x.Status)
            .SingleAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GenerateDoesNotReuseGuidanceWhenIntelligenceContextChanged()
    {
        await using var db = CreateDbContext();
        var (portfolio, targets) = await SeedPortfolioAsync(db);
        var staleGuidance = AddCompletedGuidance(db, portfolio, targets, "R4-3.0", "stale-intelligence-context");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateController(db).Generate(portfolio.Id, new ContributionRequest(4_000m), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result.Result);
        var guidances = await db.ContributionGuidances.OrderBy(x => x.GeneratedAt).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, guidances.Count);
        Assert.Equal(staleGuidance.Id, guidances[0].Id);
        Assert.Equal(ContributionIntelligenceContextKeyCalculator.Calculate(Array.Empty<GuidanceAssessment>()), guidances[1].IntelligenceContextKey);
    }

    [Fact]
    public async Task GetSeparatesR5DecisionEngineFromR4DeterministicBaseline()
    {
        await using var db = CreateDbContext();
        var (portfolio, targets) = await SeedPortfolioAsync(db);
        var guidance = AddCompletedGuidance(db, portfolio, targets, "R4-9.2");
        guidance.UpdateIntelligence("AI", "model-x", "IntelligenceAvailable", "Aloca\u00e7\u00e3o R5.", "{\"engineVersion\":\"R5-ai-allocation\"}");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateController(db).Get(portfolio.Id, guidance.Id, CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response.Value));
        var analysis = document.RootElement.GetProperty("analysis");
        Assert.Equal("R5-ai-allocation", analysis.GetProperty("decisionEngineVersion").GetString());
        Assert.Equal("R4-9.2", analysis.GetProperty("baselineEngineVersion").GetString());
    }

    [Fact]
    public async Task GetCorrectsLegacyAvoidedReasonForAClassWithoutBudget()
    {
        await using var db = CreateDbContext();
        var (portfolio, targets) = await SeedPortfolioAsync(db);
        var fiiId = Guid.NewGuid();
        var blockedId = Guid.NewGuid();
        var candidates = new[]
        {
            new ContributionCandidate(CandidateIdentity.Known(fiiId), "FIIA11", AssetClass.Fii, 100, 20, 25, 10, 0, false, []),
            new ContributionCandidate(CandidateIdentity.Known(blockedId), "FIIB11", AssetClass.Fii, 25, 5, 25, 10, 0, true, [])
        };
        var baseline = new PersistedContributionBaseline(ContributionGuidanceStatus.Calculated, portfolio.Id, 4_000m, [],
            [new(fiiId, "FIIA11", AssetClass.Fii, ContributionAvoidanceReasonType.LowerPriority, "Outro ativo da classe foi priorizado.", []),
             new(blockedId, "FIIB11", AssetClass.Fii, ContributionAvoidanceReasonType.CapitalDecisionBlock, "Ativo bloqueado.", [])],
            [], 4_000m, ContributionAllocationStyle.NoAllocation, [], [], "1", [], DateTimeOffset.UtcNow);
        var workItem = new PersistedContributionGuidanceWorkItem(baseline, candidates.Select(PersistedContributionCandidate.From).ToArray());
        var guidance = AddCompletedGuidance(db, portfolio, targets, "R4-9.2", candidatesJson: JsonSerializer.Serialize(workItem));
        var ct = TestContext.Current.CancellationToken;
        await db.SaveChangesAsync(ct);

        var result = await CreateController(db).Get(portfolio.Id, guidance.Id, ct);
        var response = Assert.IsType<OkObjectResult>(result.Result);
        var avoided = JsonSerializer.SerializeToElement(response.Value).GetProperty("avoidedCandidates");

        Assert.Equal("A classe ficou sem orçamento neste aporte.", avoided[0].GetProperty("reason").GetString());
        Assert.Equal("Ativo bloqueado.", avoided[1].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task GenerateIncludesKnownCatalogAssetWithoutCurrentPosition()
    {
        await using var db = CreateDbContext();
        var (portfolio, targets) = await SeedPortfolioAsync(db);
        var catalogAsset = new Asset("WEGE3", "WEG", AssetType.Stock, "Industrial");
        db.Assets.Add(catalogAsset);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateController(db).Generate(portfolio.Id, new ContributionRequest(4_000m), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result.Result);
        var job = await db.ContributionGuidanceJobs.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        var workItem = JsonSerializer.Deserialize<PersistedContributionGuidanceWorkItem>(job.CandidatesJson);
        Assert.NotNull(workItem);
        Assert.DoesNotContain("positions", job.CandidatesJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(workItem!.Candidates, candidate => candidate.Identity.AssetId == catalogAsset.Id && !candidate.HasPosition);
        Assert.All(workItem.Candidates, candidate => Assert.NotNull(candidate.CandidateIntelligence));
    }

    [Fact]
    public async Task GeneratePersistsCuratedStructuralMetadataBeforeCreatingCandidates()
    {
        await using var db = CreateDbContext();
        var (portfolio, _) = await SeedPortfolioAsync(db);

        var result = await CreateController(db).Generate(portfolio.Id, new ContributionRequest(4_000m), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result.Result);
        var petr4 = await db.Assets.SingleAsync(asset => asset.Ticker == "PETR4", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(EconomicSector.Energy, petr4.SectorCategory);
        Assert.Equal("BR", petr4.ListingCountryCode);
        Assert.Equal("BRL", petr4.TradingCurrency);
        Assert.Empty(petr4.GeographicExposures);
    }

    [Fact]
    public async Task GenerateBoundsPersistedCandidateContextWithoutChangingTheLegacyEvidenceCount()
    {
        await using var db = CreateDbContext();
        var (portfolio, _) = await SeedPortfolioAsync(db);
        var asset = await db.Assets.SingleAsync(x => x.Ticker == "PETR4", cancellationToken: TestContext.Current.CancellationToken);
        var publishedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var source = new Document(asset.Id, DocumentType.RelevantFact, publishedAt, "Teste", null, "https://example.test/doc", Guid.NewGuid().ToString());
        var analysis = new DocumentAnalysis(Guid.NewGuid(), source.Id, "test", "test-v1", AnalysisImportance.Informative,
            AnalysisClassification.Other, false, SuggestedAction.NoAction, "Resumo", "Impacto", "Impacto", [asset.Ticker], 1, "{}");
        db.AddRange(source, analysis);
        for (var index = 0; index < 40; index++)
        {
            var investmentEvent = new InvestmentEvent(analysis.Id, InvestmentEventType.Other, publishedAt.AddDays(-index), new string('E', 8_000), new[] { new string('F', 2_000) });
            investmentEvent.AffectedAssets.Add(asset);
            db.InvestmentEvents.Add(investmentEvent);
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateController(db).Generate(portfolio.Id, new ContributionRequest(4_000m), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result.Result);
        var job = await db.ContributionGuidanceJobs.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(job.CandidatesJson.Length > 200_000);
        var workItem = JsonSerializer.Deserialize<PersistedContributionGuidanceWorkItem>(job.CandidatesJson);
        Assert.NotNull(workItem);
        Assert.False(workItem!.CandidateIntelligencePayloadLimited);
        Assert.True(workItem.CandidatePayloadPersistedChars >= workItem.CandidatePayloadProjectedChars);
        Assert.Equal(job.CandidatesJson.Length, workItem.CandidatePayloadPersistedChars);
        Assert.Equal(40, workItem.Candidates.Sum(x => x.CandidateIntelligence!.RecentEvents.Count));
        Assert.All(workItem.Candidates.SelectMany(x => x.CandidateIntelligence!.RecentEvents), e => Assert.Equal(8_000, e.Summary.Length));
        Assert.All(workItem.Candidates.SelectMany(x => x.CandidateIntelligence!.RecentEvents), e => Assert.Equal(2_000, e.Facts.Single().Length));
        Assert.Equal(0, workItem.CandidatePayloadTruncatedEventCount);
        Assert.Equal(0, workItem.CandidatePayloadTruncatedSummaryCount);
    }

    [Fact]
    public async Task GenerateLinksAssessmentsToTheirOwnEventWithoutCopyingDocumentEvidence()
    {
        await using var db = CreateDbContext();
        var (portfolio, targets) = await SeedPortfolioAsync(db);
        var ct = TestContext.Current.CancellationToken;
        var asset = await db.Assets.SingleAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var older = AddEvent("Evento anterior", now.AddDays(-30), hasAssessment: true);
        var newer = AddEvent("Evento mais recente avaliado", now.AddDays(-20), hasAssessment: true);
        AddEvent("Evento posterior sem avaliação", now.AddDays(-5), hasAssessment: false);
        var historical = AddEvent("Provento antigo", now.AddYears(-1), hasAssessment: true, InvestmentEventType.Distribution);
        await db.SaveChangesAsync(ct);

        var result = await CreateController(db).Generate(portfolio.Id, new ContributionRequest(4_000m), ct);
        Assert.IsType<AcceptedResult>(result.Result);
        var job = await db.ContributionGuidanceJobs.SingleAsync(ct);
        var workItem = JsonSerializer.Deserialize<PersistedContributionGuidanceWorkItem>(job.CandidatesJson)!.ToRuntime();
        var intelligence = Assert.Single(workItem.Candidates).CandidateIntelligence!;

        Assert.Equal(3, intelligence.RecentEvents.Count);
        Assert.Equal(2, intelligence.Assessments.Count);
        Assert.DoesNotContain(intelligence.RecentEvents, x => x.EventId == historical.Id);
        Assert.DoesNotContain(intelligence.Assessments, x => x.InvestmentEventId == historical.Id);
        foreach (var source in new[] { older, newer })
        {
            var assessment = Assert.Single(intelligence.Assessments, x => x.InvestmentEventId == source.Id);
            var reference = Assert.Single(assessment.EvidenceReferences);
            Assert.Equal("PortfolioAssessment", reference.Type);
            Assert.Equal(source.OccurredAt, reference.OccurredAt);
            var storedEvent = Assert.Single(intelligence.RecentEvents, x => x.EventId == source.Id);
            Assert.Equal(source.Evidence.Single().DocumentId.ToString(), Assert.Single(storedEvent.Evidence).ReferenceId);
        }

        var prompt = ContributionIntelligencePrompt.BuildAllocationProposal(portfolio.Id, 4_000m, workItem.Candidates, []);
        const string contextMarker = "Contexto JSON:\n";
        using var context = JsonDocument.Parse(prompt[(prompt.IndexOf(contextMarker, StringComparison.Ordinal) + contextMarker.Length)..]);
        var promptAssessments = context.RootElement.GetProperty("eligibleCandidates")[0]
            .GetProperty("candidateIntelligence").GetProperty("assessments");
        Assert.Equal($"Avaliação de {newer.Summary}", promptAssessments[0].GetProperty("summary").GetString());
        Assert.Equal($"Avaliação de {older.Summary}", promptAssessments[1].GetProperty("summary").GetString());

        InvestmentEvent AddEvent(string summary, DateTimeOffset publishedAt, bool hasAssessment, InvestmentEventType eventType = InvestmentEventType.Other)
        {
            var sourceDoc = new Document(asset.Id, DocumentType.RelevantFact, publishedAt, "Teste", null, "https://example.test/doc", Guid.NewGuid().ToString());
            var analysis = new DocumentAnalysis(Guid.NewGuid(), sourceDoc.Id, "test", "test-v1", AnalysisImportance.Relevant,
                AnalysisClassification.Other, true, SuggestedAction.Monitor, summary, "Impacto", "Impacto", [asset.Ticker], 1, "{}");
            var evt = new InvestmentEvent(analysis.Id, eventType, hasAssessment ? publishedAt : null, summary, []);
            evt.AffectedAssets.Add(asset);
            evt.Evidence.Add(new(evt.Id, analysis.Id, sourceDoc.Id, $"Fonte de {summary}"));
            db.AddRange(sourceDoc, analysis, evt);
            if (hasAssessment)
                db.PortfolioAssessments.Add(new PortfolioAssessment(portfolio.Id, evt.Id, AttentionPriority.Medium, ActionRequirement.Monitor,
                    CapitalDecisionImpact.None, AssessmentStatus.Active, AssessmentUserState.Unread, $"Avaliação de {summary}", "Motivo", "Acompanhar",
                    IntelligenceConfidence.Medium, TargetAllocationKeyCalculator.Calculate(targets), PortfolioImpactAssessmentService.CurrentVersion,
                    PortfolioSnapshotKeyCalculator.Calculate(portfolio), evt.Version));
            return evt;
        }
    }

    [Theory]
    [InlineData(AssessmentStatus.Active, true)]
    [InlineData(AssessmentStatus.Closed, false)]
    public async Task GenerateKeepsOldLiquidationBlockWhileTheRadarAssessmentIsOpen(AssessmentStatus status, bool keepsBlock)
    {
        await using var db = CreateDbContext();
        var (portfolio, targets) = await SeedPortfolioAsync(db);
        var ct = TestContext.Current.CancellationToken;
        var asset = await db.Assets.SingleAsync(ct);
        var date = DateTimeOffset.UtcNow.AddYears(-1);
        var source = new Document(asset.Id, DocumentType.RelevantFact, date, "Teste", null, "https://example.test/doc", Guid.NewGuid().ToString());
        var analysis = new DocumentAnalysis(Guid.NewGuid(), source.Id, "test", "test-v1", AnalysisImportance.Critical,
            AnalysisClassification.Other, true, SuggestedAction.Monitor, "Liquidação", "Impacto", "Impacto", [asset.Ticker], 1, "{}");
        var liquidation = new InvestmentEvent(analysis.Id, InvestmentEventType.Liquidation, date, "Liquidação", []);
        liquidation.AffectedAssets.Add(asset);
        liquidation.Evidence.Add(new(liquidation.Id, analysis.Id, source.Id, "Liquidação"));
        var assessment = new PortfolioAssessment(portfolio.Id, liquidation.Id, AttentionPriority.Critical, ActionRequirement.Review,
            CapitalDecisionImpact.Block, status, AssessmentUserState.Unread, "Liquidação", "Motivo", "Acompanhar",
            IntelligenceConfidence.High, TargetAllocationKeyCalculator.Calculate(targets), PortfolioImpactAssessmentService.CurrentVersion,
            PortfolioSnapshotKeyCalculator.Calculate(portfolio), liquidation.Version);
        db.AddRange(source, analysis, liquidation, assessment);
        await db.SaveChangesAsync(ct);

        var result = await CreateController(db).Generate(portfolio.Id, new ContributionRequest(4_000m), ct);
        Assert.IsType<AcceptedResult>(result.Result);
        var job = await db.ContributionGuidanceJobs.SingleAsync(ct);
        var workItem = JsonSerializer.Deserialize<PersistedContributionGuidanceWorkItem>(job.CandidatesJson)!;
        var candidate = Assert.Single(workItem.Candidates);

        Assert.Equal(keepsBlock, candidate.HasBlock);
        Assert.Equal(keepsBlock, candidate.CandidateIntelligence!.RecentEvents.Any(x => x.EventId == liquidation.Id));
        Assert.Equal(keepsBlock, candidate.CandidateIntelligence.Assessments.Any(x => x.InvestmentEventId == liquidation.Id));
        Assert.Equal(!keepsBlock, workItem.Baseline.Allocations.Any());
    }

    [Fact]
    public async Task ConfirmCreatesPositionForKnownCatalogAsset()
    {
        await using var db = CreateDbContext();
        var (portfolio, _) = await SeedPortfolioAsync(db);
        var catalogAsset = new Asset("WEGE3", "WEG", AssetType.Stock, "Industrial");
        db.Assets.Add(catalogAsset);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new PortfolioTransactionsController(db).Confirm(portfolio.Id,
            new ConfirmContributionRequest(null, new[] { new ContributionPurchaseRequest(catalogAsset.Id, 10, 40) }), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        var position = await db.PortfolioPositions.SingleAsync(x => x.AssetId == catalogAsset.Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(10, position.Quantity);
        Assert.Equal(400, position.CurrentValue);
    }

    private static PrumoDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PrumoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new PrumoDbContext(options);
    }

    private static async Task<(Portfolio Portfolio, IReadOnlyList<TargetAllocation> Targets)> SeedPortfolioAsync(PrumoDbContext db)
    {
        var portfolio = new Portfolio("Carteira de teste");
        var asset = new Asset("PETR4", "Petrobras", AssetType.Stock, "Energia");
        var position = new PortfolioPosition(portfolio.Id, asset.Id, 10m, 30m, 40m);
        var target = new TargetAllocation(portfolio.Id, AssetClass.BrazilianStock, 100m);
        db.AddRange(portfolio, asset, position, target);
        await db.SaveChangesAsync();
        return (portfolio, new[] { target });
    }

    private static PersistedContributionGuidance AddCompletedGuidance(
        PrumoDbContext db,
        Portfolio portfolio,
        IReadOnlyList<TargetAllocation> targets,
        string engineVersion,
        string? intelligenceContextKey = null,
        string candidatesJson = "{}")
    {
        var guidance = new PersistedContributionGuidance(
            portfolio.Id,
            4_000m,
            PortfolioSnapshotKeyCalculator.Calculate(portfolio),
            TargetAllocationKeyCalculator.Calculate(targets)!,
            intelligenceContextKey ?? ContributionIntelligenceContextKeyCalculator.Calculate(Array.Empty<GuidanceAssessment>()),
            engineVersion,
            "test-provider",
            "test-model",
            "IntelligenceAvailable",
            "Resultado de teste.",
            "{}");
        var job = new ContributionGuidanceJob(portfolio.Id, guidance.Id, candidatesJson);
        job.Start();
        job.Complete();
        db.AddRange(guidance, job);
        return guidance;
    }

    private static ContributionGuidanceController CreateController(PrumoDbContext db)
    {
        var current = new PortfolioAssessmentCurrentService(
            db,
            new PortfolioImpactAssessmentService(),
            new PortfolioAssessmentFreshnessEvaluator(),
            NullLogger<PortfolioAssessmentCurrentService>.Instance);
        return new ContributionGuidanceController(db, current, assetMetadataCatalog: new AssetMetadataCatalogSyncService(db));
    }
}
