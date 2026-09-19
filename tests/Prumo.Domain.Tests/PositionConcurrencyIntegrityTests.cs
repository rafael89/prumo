using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Prumo.Api.Controllers;
using Prumo.Domain.Entities;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Domain.Tests;

public sealed class PositionConcurrencyIntegrityTests
{
    [RelationalFact]
    public async Task Concurrent_distinct_purchases_preserve_both_position_increments()
    {
        var admin = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=dev123";
        var database = $"prumo_integrity_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection)) await create.ExecuteNonQueryAsync();
        try
        {
            var cs = new NpgsqlConnectionStringBuilder(admin) { Database = database }.ConnectionString;
            var options = new DbContextOptionsBuilder<PrumoDbContext>().UseNpgsql(cs).Options;
            var portfolio = new Portfolio("concurrent purchases");
            var asset = new Asset("TEST3", "Test", Prumo.Domain.Enums.AssetType.Stock, null);
            await using (var seed = new PrumoDbContext(options))
            {
                await seed.Database.MigrateAsync();
                seed.AddRange(portfolio, asset, new PortfolioPosition(portfolio.Id, asset.Id, 100, 10, 10));
                await seed.SaveChangesAsync();
            }
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<ActionResult<object>> Buy(decimal quantity)
            {
                await using var db = new PrumoDbContext(new DbContextOptionsBuilder<PrumoDbContext>().UseNpgsql(cs)
                    .AddInterceptors(new SlowPositionRead()).Options);
                await gate.Task;
                return await new PortfolioTransactionsController(db).Confirm(portfolio.Id,
                    new(null, [new(asset.Id, quantity, 10)]), default);
            }
            var first = Buy(10); var second = Buy(20); gate.SetResult();
            var results = await Task.WhenAll(first, second);
            Assert.All(results, result => Assert.IsType<OkObjectResult>(result.Result));
            await using var verify = new PrumoDbContext(options);
            Assert.Equal(2, await verify.PortfolioTransactions.CountAsync());
            Assert.Equal(30, await verify.PortfolioTransactionItems.SumAsync(x => x.Quantity));
            Assert.Equal(130, (await verify.PortfolioPositions.SingleAsync()).Quantity);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    // Widen the ordinary read/modify/write overlap without imposing a barrier
    // inside the critical section (which would deadlock a correct serialized path).
    private sealed class SlowPositionRead : DbCommandInterceptor
    {
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT") && command.CommandText.Contains("\"PortfolioPositions\""))
                await Task.Delay(500, cancellationToken);
            return result;
        }
    }
}
