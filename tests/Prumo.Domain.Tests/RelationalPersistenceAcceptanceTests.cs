using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Prumo.Api.Controllers;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Domain.Tests;

public sealed class RelationalPersistenceAcceptanceTests
{
    [Fact]
    public async Task PostgreSql_clean_migrations_and_external_snapshot_round_trip()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PRUMO_RUN_RELATIONAL_TESTS"), "1", StringComparison.Ordinal))
            return;

        var admin = new NpgsqlConnectionStringBuilder("Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=dev123");
        var database = $"prumo_acceptance_{Guid.NewGuid():N}";
        await using var adminConnection = new NpgsqlConnection(admin.ConnectionString);
        await adminConnection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", adminConnection)) await create.ExecuteNonQueryAsync();
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = database }.ConnectionString;
            await using var db = CreateDb(connection);
            await db.Database.MigrateAsync();
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), x => x.EndsWith("_AddExternalContributionGuidanceSnapshot", StringComparison.Ordinal));

            var portfolio = new Portfolio("relational");
            var asset = new Asset("KNOWN3", "Known", AssetType.Stock, null);
            var guidance = new PersistedContributionGuidance(portfolio.Id, 100, "s", "t", "i", "R16.5D", "test", "test", "Available", "r", "{}");
            var identity = CandidateIdentity.FromExternal(new("Brapi", "EXT11", "B3", AssetType.Stock));
            guidance.ReplaceAllocations(new[] { new PersistedContributionGuidanceAllocation(guidance.Id, identity.Key.Value, "Brapi", "EXT11", "External", AssetClass.BrazilianStock, AssetType.Stock, "B3", "BRL", null, "verified", 100, "r", .9m, "[]") });
            db.AddRange(portfolio, asset, guidance);
            await db.SaveChangesAsync();

            await using var fresh = CreateDb(connection);
            var stored = await fresh.ContributionGuidanceAllocations.SingleAsync();
            Assert.Equal(identity.Key.Value, stored.CandidateKey);
            Assert.Equal("EXT11", stored.Ticker);
            Assert.Equal(100, stored.Amount);
            Assert.True(await fresh.Assets.AnyAsync(x => x.Ticker == "KNOWN3"));

            var first = new PortfolioTransaction(portfolio.Id, PortfolioTransactionType.Buy, DateTimeOffset.UtcNow, guidance.Id, 100);
            db.PortfolioTransactions.Add(first);
            await db.SaveChangesAsync();
            var duplicate = new PortfolioTransaction(portfolio.Id, PortfolioTransactionType.Buy, DateTimeOffset.UtcNow, guidance.Id, 100);
            db.PortfolioTransactions.Add(duplicate);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally
        {
            await using var terminate = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", adminConnection);
            await terminate.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task PostgreSql_concurrent_external_confirmations_create_one_execution()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PRUMO_RUN_RELATIONAL_TESTS"), "1", StringComparison.Ordinal)) return;
        var admin = new NpgsqlConnectionStringBuilder("Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=dev123");
        var database = $"prumo_concurrency_{Guid.NewGuid():N}";
        await using var adminConnection = new NpgsqlConnection(admin.ConnectionString);
        await adminConnection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", adminConnection)) await create.ExecuteNonQueryAsync();
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = database }.ConnectionString;
            await using (var seed = CreateDb(connection))
            {
                await seed.Database.MigrateAsync();
                var portfolio = new Portfolio("concurrency");
                var identity = CandidateIdentity.FromExternal(new("Brapi", "RACE11", "B3", AssetType.Stock));
                var guidance = new PersistedContributionGuidance(portfolio.Id, 100, "s", "t", "i", "R16.5D", "test", "test", "Available", "r", "{}");
                guidance.ReplaceAllocations(new[] { new PersistedContributionGuidanceAllocation(guidance.Id, identity.Key.Value, "Brapi", "RACE11", "Race", AssetClass.BrazilianStock, AssetType.Stock, "B3", "BRL", null, "verified", 100, "r", .9m, "[]") });
                seed.AddRange(portfolio, guidance);
                await seed.SaveChangesAsync();
                var request = new ConfirmContributionRequest(guidance.Id, new[] { new ContributionPurchaseRequest(Guid.Empty, 1, 100, identity.Key.Value) });
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var first = Confirm(connection, portfolio.Id, request, gate);
                var second = Confirm(connection, portfolio.Id, request, gate);
                gate.SetResult();
                var results = await Task.WhenAll(first, second);
                Assert.Equal(1, results.Count(x => x.Result is OkObjectResult));
                Assert.Equal(1, results.Count(x => x.Result is ConflictObjectResult));
            }
            await using var verify = CreateDb(connection);
            Assert.Single(await verify.Assets.Where(x => x.Ticker == "RACE11").ToListAsync());
            Assert.Single(await verify.PortfolioTransactions.ToListAsync());
        }
        finally
        {
            await using var terminate = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", adminConnection);
            await terminate.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task PostgreSql_confirmation_rolls_back_mid_flow_and_retry_reuses_state()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PRUMO_RUN_RELATIONAL_TESTS"), "1", StringComparison.Ordinal)) return;
        var admin = new NpgsqlConnectionStringBuilder("Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=dev123");
        var database = $"prumo_retry_{Guid.NewGuid():N}";
        await using var adminConnection = new NpgsqlConnection(admin.ConnectionString);
        await adminConnection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", adminConnection)) await create.ExecuteNonQueryAsync();
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = database }.ConnectionString;
            Guid portfolioId, guidanceId;
            var identity = CandidateIdentity.FromExternal(new("Brapi", "RETRY11", "B3", AssetType.Stock));
            await using (var seed = CreateDb(connection))
            {
                await seed.Database.MigrateAsync();
                var portfolio = new Portfolio("retry");
                var guidance = new PersistedContributionGuidance(portfolio.Id, 100, "s", "t", "i", "R16.5D", "test", "test", "Available", "r", "{}");
                guidance.ReplaceAllocations(new[] { new PersistedContributionGuidanceAllocation(guidance.Id, identity.Key.Value, "Brapi", "RETRY11", "Retry", AssetClass.BrazilianStock, AssetType.Stock, "B3", "BRL", null, "verified", 100, "r", .9m, "[]") });
                seed.AddRange(portfolio, guidance); await seed.SaveChangesAsync(); portfolioId = portfolio.Id; guidanceId = guidance.Id;
            }
            await using (var failing = CreateDb(connection, new ThrowOnSecondSaveChangesInterceptor()))
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => new PortfolioTransactionsController(failing).Confirm(portfolioId, new(guidanceId, new[] { new ContributionPurchaseRequest(Guid.Empty, 1, 100, identity.Key.Value) }), CancellationToken.None));
            }
            await using (var afterFailure = CreateDb(connection))
            {
                Assert.Empty(await afterFailure.Assets.Where(x => x.Ticker == "RETRY11").ToListAsync());
                Assert.Empty(await afterFailure.PortfolioTransactions.ToListAsync());
            }
            await using (var retry = CreateDb(connection))
            {
                var result = await new PortfolioTransactionsController(retry).Confirm(portfolioId, new(guidanceId, new[] { new ContributionPurchaseRequest(Guid.Empty, 1, 100, identity.Key.Value) }), CancellationToken.None);
                Assert.IsType<OkObjectResult>(result.Result);
            }
            await using var verify = CreateDb(connection);
            Assert.Single(await verify.Assets.Where(x => x.Ticker == "RETRY11").ToListAsync());
            Assert.Single(await verify.PortfolioTransactions.Where(x => x.ContributionGuidanceId == guidanceId).ToListAsync());
        }
        finally
        {
            await using var terminate = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", adminConnection);
            await terminate.ExecuteNonQueryAsync();
        }
    }

    private static async Task<ActionResult<object>> Confirm(string connection, Guid portfolioId, ConfirmContributionRequest request, TaskCompletionSource gate)
    {
        await gate.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var db = CreateDb(connection);
        return await new PortfolioTransactionsController(db).Confirm(portfolioId, request, CancellationToken.None);
    }

    private static PrumoDbContext CreateDb(string connection, params SaveChangesInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<PrumoDbContext>().UseNpgsql(connection).AddInterceptors(interceptors).Options);

    private sealed class ThrowOnSecondSaveChangesInterceptor : SaveChangesInterceptor
    {
        private int count;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => Interlocked.Increment(ref count) == 2 ? throw new InvalidOperationException("falha controlada antes do commit") : base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
