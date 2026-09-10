using Prumo.Api.Controllers;
using Prumo.Api.Services;
using Prumo.Application.Contributions;
using Prumo.Application.Intelligence;
using Prumo.Application.Portfolio;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Domain.Rules;
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
        await db.SaveChangesAsync();

        var result = await CreateController(db).Generate(portfolio.Id, new ContributionRequest(4_000m), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result.Result);
        var guidances = await db.ContributionGuidances.OrderBy(x => x.GeneratedAt).ToListAsync();
        Assert.Equal(2, guidances.Count);
        Assert.Equal(oldGuidance.Id, guidances[0].Id);
        Assert.Equal("R4-9.2", guidances[1].EngineVersion);
        Assert.Equal("pending-ai", guidances[1].Provider);
        Assert.Equal(ContributionGuidanceJobStatus.Queued, await db.ContributionGuidanceJobs
            .Where(x => x.ContributionGuidanceId == guidances[1].Id)
            .Select(x => x.Status)
            .SingleAsync());
    }

    [Fact]
    public async Task GenerateCreatesNewGuidanceEvenWhenCurrentVersionIsCompleted()
    {
        await using var db = CreateDbContext();
        var (portfolio, targets) = await SeedPortfolioAsync(db);
        var currentGuidance = AddCompletedGuidance(db, portfolio, targets, "R4-9.2");
        await db.SaveChangesAsync();

        var result = await CreateController(db).Generate(portfolio.Id, new ContributionRequest(4_000m), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result.Result);
        var guidances = await db.ContributionGuidances.OrderBy(x => x.GeneratedAt).ToListAsync();
        Assert.Equal(2, guidances.Count);
        Assert.Equal(currentGuidance.Id, guidances[0].Id);
        Assert.Equal("R4-9.2", guidances[1].EngineVersion);
        Assert.Equal("pending-ai", guidances[1].Provider);
        Assert.Equal(ContributionGuidanceJobStatus.Queued, await db.ContributionGuidanceJobs
            .Where(x => x.ContributionGuidanceId == guidances[1].Id)
            .Select(x => x.Status)
            .SingleAsync());
    }

    [Fact]
    public async Task GenerateDoesNotReuseGuidanceWhenIntelligenceContextChanged()
    {
        await using var db = CreateDbContext();
        var (portfolio, targets) = await SeedPortfolioAsync(db);
        var staleGuidance = AddCompletedGuidance(db, portfolio, targets, "R4-3.0", "stale-intelligence-context");
        await db.SaveChangesAsync();

        var result = await CreateController(db).Generate(portfolio.Id, new ContributionRequest(4_000m), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result.Result);
        var guidances = await db.ContributionGuidances.OrderBy(x => x.GeneratedAt).ToListAsync();
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
        await db.SaveChangesAsync();

        var result = await CreateController(db).Get(portfolio.Id, guidance.Id, CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response.Value));
        var analysis = document.RootElement.GetProperty("analysis");
        Assert.Equal("R5-ai-allocation", analysis.GetProperty("decisionEngineVersion").GetString());
        Assert.Equal("R4-9.2", analysis.GetProperty("baselineEngineVersion").GetString());
    }

    [Fact]
    public async Task GenerateIncludesKnownCatalogAssetWithoutCurrentPosition()
    {
        await using var db = CreateDbContext();
        var (portfolio, targets) = await SeedPortfolioAsync(db);
        var catalogAsset = new Asset("WEGE3", "WEG", AssetType.Stock, "Industrial");
        db.Assets.Add(catalogAsset);
        await db.SaveChangesAsync();

        var result = await CreateController(db).Generate(portfolio.Id, new ContributionRequest(4_000m), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result.Result);
        var job = await db.ContributionGuidanceJobs.SingleAsync();
        var workItem = JsonSerializer.Deserialize<ContributionGuidanceWorkItem>(job.CandidatesJson);
        Assert.NotNull(workItem);
        Assert.Contains(workItem!.Candidates, candidate => candidate.AssetId == catalogAsset.Id && !candidate.HasPosition);
        Assert.All(workItem.Candidates, candidate => Assert.NotNull(candidate.CandidateIntelligence));
    }

    [Fact]
    public async Task GeneratePersistsCuratedStructuralMetadataBeforeCreatingCandidates()
    {
        await using var db = CreateDbContext();
        var (portfolio, _) = await SeedPortfolioAsync(db);

        var result = await CreateController(db).Generate(portfolio.Id, new ContributionRequest(4_000m), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result.Result);
        var petr4 = await db.Assets.SingleAsync(asset => asset.Ticker == "PETR4");
        Assert.Equal(EconomicSector.Energy, petr4.SectorCategory);
        Assert.Equal("BR", petr4.ListingCountryCode);
        Assert.Equal("BRL", petr4.TradingCurrency);
        Assert.Empty(petr4.GeographicExposures);
    }

    [Fact]
    public async Task ConfirmCreatesPositionForKnownCatalogAsset()
    {
        await using var db = CreateDbContext();
        var (portfolio, _) = await SeedPortfolioAsync(db);
        var catalogAsset = new Asset("WEGE3", "WEG", AssetType.Stock, "Industrial");
        db.Assets.Add(catalogAsset);
        await db.SaveChangesAsync();

        var result = await new PortfolioTransactionsController(db).Confirm(portfolio.Id,
            new ConfirmContributionRequest(null, new[] { new ContributionPurchaseRequest(catalogAsset.Id, 10, 40) }), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        var position = await db.PortfolioPositions.SingleAsync(x => x.AssetId == catalogAsset.Id);
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
        string? intelligenceContextKey = null)
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
        var job = new ContributionGuidanceJob(portfolio.Id, guidance.Id, "{}");
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
