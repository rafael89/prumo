using Prumo.Application.Contributions;
using Prumo.Application.Portfolio;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class ContributionReserveIntegrityTests
{
    [Theory]
    [InlineData(200, 0)]
    [InlineData(1000, 200)]
    [InlineData(2000, 800)]
    public void Missing_classes_are_observations_and_do_not_become_generic_allocations(decimal amount, decimal stocks)
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
        Assert.Empty(result.ClassAllocations);
        Assert.Equal(amount - result.AllocatedAmount, result.UnallocatedAmount);
        Assert.Contains(result.Reasons, reason => reason.Contains("não possui ativo elegível cadastrado", StringComparison.OrdinalIgnoreCase));
        var repeated = calculator.Calculate(input);
        Assert.Equal(result.ClassAllocations, repeated.ClassAllocations);
        Assert.Equal(result.Allocations.Select(x => (x.AssetId, x.Amount)), repeated.Allocations.Select(x => (x.AssetId, x.Amount)));
        Assert.Empty(ContributionIntelligenceApplier.ApplyFallback(result, "provider unavailable").ClassAllocations);
    }
}
