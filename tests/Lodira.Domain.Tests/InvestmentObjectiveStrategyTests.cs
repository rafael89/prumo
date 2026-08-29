using Lodira.Domain.Entities;
using Lodira.Domain.Enums;

namespace Lodira.Domain.Tests;

public sealed class InvestmentObjectiveStrategyTests
{
    [Fact]
    public void Cria_objetivo_valido_com_meta_opcional()
    {
        var objective = new InvestmentObjective(Guid.NewGuid(), InvestmentGoal.WealthGrowth, TargetMetric.None, null, TimeHorizon.LongTerm, 1);
        Assert.Equal(InvestmentGoal.WealthGrowth, objective.PrimaryGoal);
        Assert.Null(objective.TargetAmount);
    }

    [Fact]
    public void Rejeita_meta_sem_metrica()
        => Assert.Throws<ArgumentException>(() => new InvestmentObjective(Guid.NewGuid(), InvestmentGoal.PassiveIncome, TargetMetric.None, 10000, TimeHorizon.LongTerm, 1));

    [Fact]
    public void Rejeita_metrica_sem_meta_positiva()
        => Assert.Throws<ArgumentException>(() => new InvestmentObjective(Guid.NewGuid(), InvestmentGoal.PassiveIncome, TargetMetric.MonthlyPassiveIncome, null, TimeHorizon.LongTerm, 1));

    [Fact]
    public void Objetivo_anterior_pode_ser_preservado_ao_criar_nova_versao()
    {
        var portfolioId = Guid.NewGuid();
        var first = new InvestmentObjective(portfolioId, InvestmentGoal.PassiveIncome, TargetMetric.MonthlyPassiveIncome, 5000, TimeHorizon.LongTerm, 1);
        first.Archive();
        var second = new InvestmentObjective(portfolioId, InvestmentGoal.PassiveIncome, TargetMetric.MonthlyPassiveIncome, 10000, TimeHorizon.LongTerm, 2);

        Assert.NotNull(first.ArchivedAt);
        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);
        Assert.Equal(5000, first.TargetAmount);
    }

    [Fact]
    public void Cria_estrategia_com_prioridades_politica_e_limite_opcional()
    {
        var strategy = new PortfolioAllocationStrategy(Guid.NewGuid(), "Renda", "passive-income", 1, PriorityLevel.High, PriorityLevel.Medium, PriorityLevel.Medium, ContributionPolicy.CorrectImbalancesFirst, 25);
        Assert.Equal(PriorityLevel.High, strategy.IncomePriority);
        Assert.Equal(ContributionPolicy.CorrectImbalancesFirst, strategy.ContributionPolicy);
        Assert.Equal(25, strategy.MaximumAssetWeight);
    }

    [Fact]
    public void Rejeita_limite_de_ativo_invalido()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new PortfolioAllocationStrategy(Guid.NewGuid(), "Renda", null, 1, PriorityLevel.Low, PriorityLevel.Low, PriorityLevel.Low, ContributionPolicy.MaintainTargetAllocation, 101));

    [Fact]
    public void Rejeita_alocacao_que_nao_soma_cem()
        => Assert.Throws<ArgumentException>(() => PortfolioAllocationStrategy.ValidateAllocations(new[] { (AssetClass.Fii, 40m), (AssetClass.BrazilianStock, 40m) }));

    [Fact]
    public void Estrategia_anterior_permanece_preservada_ao_versionar()
    {
        var first = new PortfolioAllocationStrategy(Guid.NewGuid(), "Renda v1", null, 1, PriorityLevel.High, PriorityLevel.Medium, PriorityLevel.Medium, ContributionPolicy.CorrectImbalancesFirst, null);
        first.Archive();
        var second = new PortfolioAllocationStrategy(first.PortfolioId, "Renda v2", null, 2, PriorityLevel.Medium, PriorityLevel.High, PriorityLevel.Medium, ContributionPolicy.MaintainTargetAllocation, null);

        Assert.NotNull(first.ArchivedAt);
        Assert.Equal("Renda v1", first.Name);
        Assert.Equal(PriorityLevel.High, first.IncomePriority);
        Assert.Equal(2, second.Version);
    }
}
