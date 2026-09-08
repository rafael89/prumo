using Prumo.Domain.Entities;
using Prumo.Domain.Rules;

namespace Prumo.Domain.Tests;

public sealed class PortfolioCalculatorTests
{
    [Fact]
    public void Calcula_valor_da_posicao_com_cotacao()
    {
        var value = PortfolioPosition.CalculateCurrentValue(10, 25.50m);

        Assert.Equal(255m, value);
    }

    [Fact]
    public void Calcula_patrimonio_com_soma_das_posicoes_com_cotacao()
    {
        var positions = new[]
        {
            new PortfolioPosition(Guid.NewGuid(), Guid.NewGuid(), 10, 20, 25),
            new PortfolioPosition(Guid.NewGuid(), Guid.NewGuid(), 5, 30, 40)
        };

        Assert.Equal(450m, PortfolioCalculator.CalculateTotalValue(positions));
    }

    [Fact]
    public void Calcula_peso_da_posicao_sobre_o_patrimonio()
    {
        var positions = new[]
        {
            new PortfolioPosition(Guid.NewGuid(), Guid.NewGuid(), 10, 20, 25),
            new PortfolioPosition(Guid.NewGuid(), Guid.NewGuid(), 5, 30, 40)
        };

        PortfolioCalculator.RecalculateWeights(positions);

        Assert.Equal(55.5555555555555555555555555556m, positions[0].PortfolioWeight, 20);
        Assert.Equal(44.4444444444444444444444444444m, positions[1].PortfolioWeight, 20);
    }

    [Fact]
    public void Uma_unica_posicao_com_valor_tem_peso_de_cem_porcento()
    {
        var position = new PortfolioPosition(Guid.NewGuid(), Guid.NewGuid(), 30, 80, 80);

        PortfolioCalculator.RecalculateWeights(new[] { position });

        Assert.Equal(100m, position.PortfolioWeight);
    }

    [Fact]
    public void Quantidade_zero_produz_valor_zero()
    {
        var position = new PortfolioPosition(Guid.NewGuid(), Guid.NewGuid(), 0, 20, 25);

        Assert.Equal(0m, position.CurrentValue);
    }

    [Fact]
    public void Carteira_sem_posicoes_tem_patrimonio_zero()
    {
        Assert.Equal(0m, PortfolioCalculator.CalculateTotalValue(Array.Empty<PortfolioPosition>()));
    }

    [Fact]
    public void Posicao_sem_cotacao_nao_inventa_valor_e_recebe_peso_zero()
    {
        var position = new PortfolioPosition(Guid.NewGuid(), Guid.NewGuid(), 10, 20, null);

        PortfolioCalculator.RecalculateWeights(new[] { position });

        Assert.Null(position.CurrentValue);
        Assert.Equal(0m, position.PortfolioWeight);
    }
}
