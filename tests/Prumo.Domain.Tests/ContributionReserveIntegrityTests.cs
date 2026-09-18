using Prumo.Application.Contributions;
using Prumo.Application.Portfolio;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class ContributionReserveIntegrityTests
{
    [Theory]
    [InlineData(200, 0, 200, 0)]
    [InlineData(1000, 200, 600, 200)]
    [InlineData(2000, 800, 900, 300)]
    public void Reserves_reduce_remaining_need_and_never_repeat_an_already_funded_need(decimal amount, decimal stocks, decimal fiis, decimal international)
    {
        var health = new PortfolioHealthResult(PortfolioHealthStatus.Unbalanced,
            [new(AssetClass.BrazilianStock, 100, 60, 40, 40, AllocationDeviationDirection.Overweight),
             new(AssetClass.Fii, 0, 30, -30, 30, AllocationDeviationDirection.Underweight),
             new(AssetClass.International, 0, 10, -10, 10, AllocationDeviationDirection.Underweight)], null, [], "1", DateTimeOffset.UtcNow);
        var input = new ContributionGuidanceInput(Guid.NewGuid(), amount, health,
            [new(Guid.NewGuid(), "AAAA3", AssetClass.BrazilianStock, 1000, 100)], []);
        var calculator = new ContributionGuidanceCalculator();
        var result = calculator.Calculate(input);
        Assert.Equal(stocks, result.Allocations.Sum(x => x.Amount));
        Assert.Equal(fiis, result.ClassAllocations.Where(x => x.AssetClass == AssetClass.Fii).Sum(x => x.Amount));
        Assert.Equal(international, result.ClassAllocations.Where(x => x.AssetClass == AssetClass.International).Sum(x => x.Amount));
        Assert.Equal(amount, result.AllocatedAmount); Assert.Equal(0, result.UnallocatedAmount);
        Assert.All(result.ClassAllocations, allocation => Assert.True(allocation.Amount <= (1000 + amount) * (allocation.AssetClass == AssetClass.Fii ? .3m : .1m)));
        var repeated = calculator.Calculate(input);
        Assert.Equal(result.ClassAllocations, repeated.ClassAllocations);
        Assert.Equal(result.Allocations.Select(x => (x.AssetId, x.Amount)), repeated.Allocations.Select(x => (x.AssetId, x.Amount)));
        Assert.Equal(result.ClassAllocations, ContributionIntelligenceApplier.ApplyFallback(result, "provider unavailable").ClassAllocations);
    }
}
