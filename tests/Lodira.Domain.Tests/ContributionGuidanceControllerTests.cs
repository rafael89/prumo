using Lodira.Api.Controllers;
using Lodira.Api.Services;
using Lodira.Application.Contributions;
using Lodira.Application.Intelligence;
using Lodira.Application.Portfolio;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Domain.Rules;
using Lodira.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace Lodira.Domain.Tests;

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
        Assert.Equal("R4-8.0", guidances[1].EngineVersion);
        Assert.Equal("pending-ai", guidances[1].Provider);
        Assert.Equal(ContributionGuidanceJobStatus.Queued, await db.ContributionGuidanceJobs
            .Where(x => x.ContributionGuidanceId == guidances[1].Id)
            .Select(x => x.Status)
            .SingleAsync());
    }

    [Fact]
    public async Task GenerateReusesCompletedGuidanceFromCurrentEngineVersion()
    {
        await using var db = CreateDbContext();
        var (portfolio, targets) = await SeedPortfolioAsync(db);
        var currentGuidance = AddCompletedGuidance(db, portfolio, targets, "R4-8.0");
        await db.SaveChangesAsync();

        var result = await CreateController(db).Generate(portfolio.Id, new ContributionRequest(4_000m), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(await db.ContributionGuidances.ToListAsync());
        Assert.Equal(currentGuidance.Id, await db.ContributionGuidances.Select(x => x.Id).SingleAsync());
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

    private static LodiraDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<LodiraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LodiraDbContext(options);
    }

    private static async Task<(Portfolio Portfolio, IReadOnlyList<TargetAllocation> Targets)> SeedPortfolioAsync(LodiraDbContext db)
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
        LodiraDbContext db,
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

    private static ContributionGuidanceController CreateController(LodiraDbContext db)
    {
        var current = new PortfolioAssessmentCurrentService(
            db,
            new PortfolioImpactAssessmentService(),
            new PortfolioAssessmentFreshnessEvaluator(),
            NullLogger<PortfolioAssessmentCurrentService>.Instance);
        return new ContributionGuidanceController(db, current);
    }
}
