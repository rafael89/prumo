using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Prumo.Api.Services;
using Prumo.Application.Intelligence;
using Prumo.Application.Portfolio;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Domain.Tests;

public sealed class PortfolioAssessmentConcurrencyTests
{
    [RelationalFact]
    public async Task Concurrent_refreshes_return_the_same_persisted_assessment()
    {
        var admin = new NpgsqlConnectionStringBuilder("Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=dev123");
        var database = $"prumo_assessment_{Guid.NewGuid():N}";
        await using var adminConnection = new NpgsqlConnection(admin.ConnectionString);
        await adminConnection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", adminConnection))
            await create.ExecuteNonQueryAsync();

        try
        {
            var connection = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = database }.ConnectionString;
            await using (var seed = CreateDb(connection))
            {
                await seed.Database.MigrateAsync();
                var asset = new Asset("RACE11", "Ativo teste", AssetType.Stock, "Teste");
                var portfolio = new Portfolio("Carteira teste");
                portfolio.Positions.Add(new PortfolioPosition(portfolio.Id, asset.Id, 10m, 100m, 10m));
                var document = new Document(asset.Id, DocumentType.Other, DateTimeOffset.UtcNow, "teste", "teste-1", "https://example.test/teste", Guid.NewGuid().ToString("N"));
                var analysis = new DocumentAnalysis(Guid.NewGuid(), document.Id, "test", "test-v1", AnalysisImportance.Relevant, AnalysisClassification.Other, false, SuggestedAction.NoAction, "Resumo", "Impacto", "Impacto", new[] { asset.Ticker }, 1m, "{}");
                var investmentEvent = new InvestmentEvent(analysis.Id, InvestmentEventType.Other, document.PublishedAt, "Evento teste", Array.Empty<string>());
                investmentEvent.AffectedAssets.Add(asset);
                var target = new TargetAllocation(portfolio.Id, AssetClass.BrazilianStock, 100m);
                seed.AddRange(asset, portfolio, document, analysis, investmentEvent, target);
                await seed.SaveChangesAsync();
                seed.PortfolioAssessments.Add(new PortfolioAssessment(portfolio.Id, investmentEvent.Id, AttentionPriority.Low, ActionRequirement.None, CapitalDecisionImpact.None, AssessmentStatus.Detected, AssessmentUserState.Unread, "Resumo antigo", "Motivo antigo", "Acompanhar", IntelligenceConfidence.Medium, "old-target", "old-version", "old-snapshot", investmentEvent.Version));
                await seed.SaveChangesAsync();
            }

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<Guid> RefreshAsync()
            {
                await using var db = CreateDb(connection);
                var portfolio = await db.Portfolios.AsNoTracking().Include(x => x.Positions).ThenInclude(x => x.Asset).SingleAsync();
                var targets = await db.TargetAllocations.AsNoTracking().ToListAsync();
                var assessments = await db.PortfolioAssessments.AsNoTracking().Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets).ToListAsync();
                await gate.Task;
                var current = await new PortfolioAssessmentCurrentService(db, new PortfolioImpactAssessmentService(), new PortfolioAssessmentFreshnessEvaluator(), NullLogger<PortfolioAssessmentCurrentService>.Instance)
                    .EnsureFreshAsync(portfolio, targets, assessments, CancellationToken.None);
                return Assert.Single(current).Id;
            }

            var first = RefreshAsync();
            var second = RefreshAsync();
            gate.SetResult();
            var ids = await Task.WhenAll(first, second);
            Assert.Equal(ids[0], ids[1]);

            await using var verify = CreateDb(connection);
            Assert.Equal(2, await verify.PortfolioAssessments.CountAsync());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", adminConnection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static PrumoDbContext CreateDb(string connection)
        => new(new DbContextOptionsBuilder<PrumoDbContext>().UseNpgsql(connection).Options);
}
