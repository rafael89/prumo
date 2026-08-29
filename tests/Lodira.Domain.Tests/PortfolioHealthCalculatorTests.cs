using Lodira.Application.Portfolio;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;

namespace Lodira.Domain.Tests;

public sealed class PortfolioHealthCalculatorTests
{
    private static readonly PortfolioHealthCalculator Calculator = new();

    [Fact]
    public void Strategy_inexistente_resulta_NotConfigured()
    {
        var result = Calculator.Calculate(new Portfolio("Teste"), null);
        Assert.Equal(PortfolioHealthStatus.NotConfigured, result.Status);
        Assert.Null(result.StrategyVersion);
    }

    [Fact]
    public void Strategy_existente_sem_posicoes_resulta_EmptyPortfolio()
    {
        var result = Calculator.Calculate(new Portfolio("Teste"), Strategy(100, AssetClass.Fii));
        Assert.Equal(PortfolioHealthStatus.EmptyPortfolio, result.Status);
    }

    [Theory]
    [InlineData(2, "Aligned")]
    [InlineData(5, "SlightlyUnbalanced")]
    [InlineData(10, "Unbalanced")]
    [InlineData(10.01, "StronglyUnbalanced")]
    public void Classifica_nos_limites_da_policy(decimal deviation, string expected)
    {
        var portfolio = WithPosition(100, AssetClass.Fii);
        var result = Calculator.Calculate(portfolio, Strategy(100 - deviation, AssetClass.Fii));
        Assert.Equal(expected, result.Status.ToString());
    }

    [Fact]
    public void Calcula_underweight_overweight_aligned_e_classe_ausente_na_strategy()
    {
        var portfolio = new Portfolio("Teste");
        AddPosition(portfolio, 30, AssetClass.Fii);
        AddPosition(portfolio, 70, AssetClass.BrazilianStock);
        var strategy = Strategy(60, AssetClass.BrazilianStock, (AssetClass.Fii, 30), (AssetClass.FixedIncome, 10));

        var result = Calculator.Calculate(portfolio, strategy);

        Assert.Equal(3, result.AllocationDeviations.Count);
        Assert.Equal(AllocationDeviationDirection.Underweight, result.AllocationDeviations.Single(x => x.AssetClass == AssetClass.FixedIncome).Direction);
        Assert.Equal(AllocationDeviationDirection.Overweight, result.AllocationDeviations.Single(x => x.AssetClass == AssetClass.BrazilianStock).Direction);
        Assert.Equal(AllocationDeviationDirection.Aligned, result.AllocationDeviations.Single(x => x.AssetClass == AssetClass.Fii).Direction);
        Assert.Equal(60, result.AllocationDeviations.Single(x => x.AssetClass == AssetClass.BrazilianStock).TargetPercentage);
    }

    [Fact]
    public void MainDeviation_usa_desempate_por_nome_da_classe()
    {
        var portfolio = new Portfolio("Teste");
        AddPosition(portfolio, 50, AssetClass.BrazilianStock);
        AddPosition(portfolio, 50, AssetClass.Fii);
        var result = Calculator.Calculate(portfolio, Strategy(40, AssetClass.BrazilianStock, (AssetClass.Fii, 60)));
        Assert.Equal(AssetClass.BrazilianStock, result.MainDeviation!.AssetClass);
    }

    [Fact]
    public void CapitalNeeds_contem_somente_underweight_e_ordena_por_deficit()
    {
        var portfolio = new Portfolio("Teste");
        AddPosition(portfolio, 100, AssetClass.BrazilianStock);
        var result = Calculator.Calculate(portfolio, Strategy(40, AssetClass.BrazilianStock, (AssetClass.Fii, 30), (AssetClass.FixedIncome, 30)));
        Assert.Equal(new[] { AssetClass.Fii, AssetClass.FixedIncome }, result.CapitalNeeds.Select(x => x.AssetClass));
        Assert.DoesNotContain(result.CapitalNeeds, x => x.AssetClass == AssetClass.BrazilianStock);
    }

    [Fact]
    public void Concentration_respeita_limite_opcional_sem_inventar_limite()
    {
        var portfolio = WithPosition(100, AssetClass.Fii);
        var withoutLimit = Calculator.Calculate(portfolio, Strategy(100, AssetClass.Fii));
        Assert.False(withoutLimit.Concentrations.Single().ExceedsConfiguredMaximum);
        var withLimit = Calculator.Calculate(portfolio, Strategy(100, AssetClass.Fii, Array.Empty<(AssetClass, decimal)>(), 100));
        Assert.False(withLimit.Concentrations.Single().ExceedsConfiguredMaximum);
        var exceeded = Calculator.Calculate(portfolio, Strategy(100, AssetClass.Fii, Array.Empty<(AssetClass, decimal)>(), 99.99m));
        Assert.True(exceeded.Concentrations.Single().ExceedsConfiguredMaximum);
    }

    private static PortfolioAllocationStrategy Strategy(decimal firstTarget, AssetClass firstClass, params (AssetClass Class, decimal Target)[] additional)
        => Strategy(firstTarget, firstClass, additional, null);

    private static PortfolioAllocationStrategy Strategy(decimal firstTarget, AssetClass firstClass, (AssetClass Class, decimal Target)[] additional, decimal? maximum)
    {
        var strategy = new PortfolioAllocationStrategy(Guid.NewGuid(), "Teste", null, 3, PriorityLevel.Medium, PriorityLevel.Medium, PriorityLevel.Medium, ContributionPolicy.CorrectImbalancesFirst, maximum);
        strategy.Allocations.Add(new StrategyAllocation(strategy.Id, firstClass, firstTarget));
        foreach (var item in additional) strategy.Allocations.Add(new StrategyAllocation(strategy.Id, item.Class, item.Target));
        return strategy;
    }

    private static Portfolio WithPosition(decimal value, AssetClass assetClass)
    {
        var portfolio = new Portfolio("Teste");
        AddPosition(portfolio, value, assetClass);
        return portfolio;
    }

    private static void AddPosition(Portfolio portfolio, decimal value, AssetClass assetClass)
    {
        var asset = new Asset($"T{portfolio.Positions.Count + 1}AA", "Teste", AssetType.Other, null, assetClass);
        var position = new PortfolioPosition(portfolio.Id, asset.Id, 1, value, value);
        typeof(PortfolioPosition).GetProperty(nameof(PortfolioPosition.Asset))!.SetValue(position, asset);
        portfolio.Positions.Add(position);
    }
}
