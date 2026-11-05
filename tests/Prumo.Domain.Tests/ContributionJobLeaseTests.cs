using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Worker;

namespace Prumo.Domain.Tests;

public sealed class ContributionJobLeaseTests
{
    [RelationalFact]
    public async Task Expired_attempt_cannot_publish_or_requeue_the_new_owner()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var firstDb = fixture.CreateDb();
        var first = (await ContributionGuidanceWorker.ClaimNextAsync(firstDb, default))!;
        Assert.Equal(1, first.Attempts);
        await using var secondDb = fixture.CreateDb();
        var second = (await ContributionGuidanceWorker.ClaimNextAsync(secondDb, default, DateTimeOffset.UtcNow.AddHours(1)))!;
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(2, second.Attempts);

        using var worker = fixture.CreateWorker();
        var publication = new ContributionGuidanceWorker.ContributionGuidancePublication("test", "test", "Available", "new result", "{}", []);
        Assert.False(await worker.PublishAsync(first.Id, first.Attempts, publication, default));
        await worker.RecoverClaimedJobAsync(first.Id, first.Attempts, "stale failure", default);
        await using var verify = fixture.CreateDb();
        var active = await verify.ContributionGuidanceJobs.AsNoTracking().SingleAsync();
        Assert.Equal(ContributionGuidanceJobStatus.Processing, active.Status);
        Assert.Equal(2, active.Attempts);
        Assert.Equal("Pending", (await verify.ContributionGuidances.AsNoTracking().SingleAsync()).IntelligenceAvailability);

        Assert.True(await worker.PublishAsync(second.Id, second.Attempts, publication, default));
        Assert.Equal(ContributionGuidanceJobStatus.Completed, (await verify.ContributionGuidanceJobs.AsNoTracking().SingleAsync()).Status);
        Assert.Equal("new result", (await verify.ContributionGuidances.AsNoTracking().SingleAsync()).Reasoning);
    }

    [RelationalFact]
    public async Task Expired_jobs_stop_after_three_attempts()
    {
        await using var fixture = await Fixture.CreateAsync();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await using var db = fixture.CreateDb();
            var job = await ContributionGuidanceWorker.ClaimNextAsync(db, default, DateTimeOffset.UtcNow.AddHours(1));
            Assert.Equal(attempt, job!.Attempts);
        }
        await using var exhaustedDb = fixture.CreateDb();
        Assert.Null(await ContributionGuidanceWorker.ClaimNextAsync(exhaustedDb, default, DateTimeOffset.UtcNow.AddHours(1)));
        var exhausted = await exhaustedDb.ContributionGuidanceJobs.AsNoTracking().SingleAsync();
        Assert.Equal(ContributionGuidanceJobStatus.Failed, exhausted.Status);
        Assert.Equal(3, exhausted.Attempts);
    }

    [RelationalFact]
    public async Task Concurrent_claims_have_a_single_owner()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var first = fixture.CreateDb();
        await using var second = fixture.CreateDb();
        var claims = await Task.WhenAll(ContributionGuidanceWorker.ClaimNextAsync(first, default),
            ContributionGuidanceWorker.ClaimNextAsync(second, default));
        Assert.Single(claims, x => x is not null);
    }

    private sealed class Fixture(string connection, string database, ServiceProvider services) : IAsyncDisposable
    {
        private const string AdminConnection = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=dev123";
        public PrumoDbContext CreateDb() => new(new DbContextOptionsBuilder<PrumoDbContext>().UseNpgsql(connection).Options);
        public ContributionGuidanceWorker CreateWorker() => new(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ContributionGuidanceWorker>.Instance, new WorkerMetrics());

        public static async Task<Fixture> CreateAsync()
        {
            var database = $"prumo_lease_{Guid.NewGuid():N}";
            await using var admin = new NpgsqlConnection(AdminConnection);
            await admin.OpenAsync();
            await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin)) await command.ExecuteNonQueryAsync();
            var connection = new NpgsqlConnectionStringBuilder(AdminConnection) { Database = database }.ConnectionString;
            var services = new ServiceCollection().AddDbContext<PrumoDbContext>(options => options.UseNpgsql(connection)).BuildServiceProvider();
            var fixture = new Fixture(connection, database, services);
            try
            {
                await using var db = fixture.CreateDb();
                await db.Database.MigrateAsync();
                var portfolio = new Portfolio("lease");
                var guidance = new PersistedContributionGuidance(portfolio.Id, 100, "s", "t", "i", "test", "test", "test", "Pending", "pending", "{}");
                db.AddRange(portfolio, guidance, new ContributionGuidanceJob(portfolio.Id, guidance.Id, "{}"));
                await db.SaveChangesAsync();
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await using var admin = new NpgsqlConnection(AdminConnection);
            await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", admin);
            await command.ExecuteNonQueryAsync();
        }
    }
}
