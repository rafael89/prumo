using Prumo.Domain.Entities;
using Prumo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Prumo.Domain.Tests;

public sealed class PortfolioTransactionTests
{
    [Fact]
    public void Imported_transaction_fingerprint_is_deterministic_and_scoped_to_portfolio()
    {
        var portfolio = Guid.NewGuid();
        var asset = Guid.NewGuid();
        var date = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        var first = PortfolioTransaction.CreateImportFingerprint(portfolio, asset, date, 10m, 25.5m, "Corretora");
        var same = PortfolioTransaction.CreateImportFingerprint(portfolio, asset, date, 10m, 25.5m, "Corretora");
        var otherPortfolio = PortfolioTransaction.CreateImportFingerprint(Guid.NewGuid(), asset, date, 10m, 25.5m, "Corretora");

        Assert.Equal(first, same);
        Assert.NotEqual(first, otherPortfolio);
    }

    [Fact]
    public void Imported_transaction_fingerprint_has_a_unique_database_index()
    {
        var options = new DbContextOptionsBuilder<PrumoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new PrumoDbContext(options);

        var index = db.Model.FindEntityType(typeof(PortfolioTransaction))!.GetIndexes()
            .Single(index => index.Properties.Any(property => property.Name == nameof(PortfolioTransaction.ImportFingerprint)));

        Assert.True(index.IsUnique);
        Assert.Contains("ImportFingerprint", index.GetFilter());
    }
}
