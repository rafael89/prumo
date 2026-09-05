using Prumo.Infrastructure.MarketData;
using Prumo.Infrastructure.PortfolioImports;

namespace Prumo.Domain.Tests;

public sealed class MarketApiRemovalAcceptanceTests
{
    [Fact]
    public void B3_import_requires_no_market_provider()
    {
        Assert.NotNull(new B3XlsxPortfolioImporter());
        Assert.DoesNotContain(typeof(MarketDataService).Assembly.GetTypes(), type =>
            type.Name.Contains("Brapi", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("AlphaVantage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Prumo_starts_without_market_provider_configuration()
    {
        var constructor = typeof(MarketDataService).GetConstructors().Single();
        Assert.Single(constructor.GetParameters());
        Assert.Equal("db", constructor.GetParameters()[0].Name);
    }
}
