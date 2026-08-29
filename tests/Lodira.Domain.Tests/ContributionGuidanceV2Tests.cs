using Lodira.Application.Contributions;
using Lodira.Application.Portfolio;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;

namespace Lodira.Domain.Tests;

public sealed class ContributionGuidanceV2Tests
{
    [Fact]
    public void Block_nao_recebe_capital_e_Favor_nao_cria_necessidade()
    {
        var strategy = Strategy(ContributionPolicy.CorrectImbalancesFirst);
        var positions = new[] { new GuidancePosition(Guid.NewGuid(), "BLOCK3", AssetClass.Fii, 100, 10), new GuidancePosition(Guid.NewGuid(), "FAVOR3", AssetClass.BrazilianStock, 100, 10) };
        var health = Health(new CapitalNeed(AssetClass.Fii, 50, 60, 10, 1), new CapitalNeed(AssetClass.BrazilianStock, 10, 30, 20, 2));
        var assessments = new[] { new GuidanceAssessment(positions[0].AssetId, CapitalDecisionImpact.Block, AssessmentStatus.Active, null, "v1"), new GuidanceAssessment(positions[1].AssetId, CapitalDecisionImpact.Favor, AssessmentStatus.Active, null, "v1") };
        var result = Calculate(strategy, health, positions, assessments, 50);
        Assert.DoesNotContain(result.Allocations, x => x.AssetId == positions[0].AssetId);
        Assert.Contains(result.Allocations, x => x.AssetId == positions[1].AssetId);
        Assert.Contains(result.AvoidedCandidates, x => x.ReasonType == ContributionAvoidanceReasonType.CapitalDecisionBlock);
    }

    [Fact]
    public void Caution_eh_elegivel_e_None_eh_priorizado()
    {
        var strategy = Strategy(ContributionPolicy.CorrectImbalancesFirst);
        var low = new GuidancePosition(Guid.NewGuid(), "LOW3", AssetClass.Fii, 100, 10);
        var high = new GuidancePosition(Guid.NewGuid(), "HIGH3", AssetClass.Fii, 200, 20);
        var result = Calculate(strategy, Health(new CapitalNeed(AssetClass.Fii, 10, 40, 30, 1)), new[] { low, high }, new[] { new GuidanceAssessment(low.AssetId, CapitalDecisionImpact.Caution, AssessmentStatus.Active, null, "v1") }, 50);
        Assert.Contains(result.Allocations, x => x.AssetId == high.AssetId);
        Assert.DoesNotContain(result.Allocations, x => x.AssetId == low.AssetId);
    }

    [Fact]
    public void Limite_maximo_produz_alocacao_parcial_sem_ultrapassar_peso()
    {
        var strategy = Strategy(ContributionPolicy.CorrectImbalancesFirst, 15);
        var position = new GuidancePosition(Guid.NewGuid(), "LIMIT3", AssetClass.Fii, 140, 14);
        var other = new GuidancePosition(Guid.NewGuid(), "OTHER3", AssetClass.BrazilianStock, 860, 86);
        var result = Calculate(strategy, Health(new CapitalNeed(AssetClass.Fii, 14, 40, 26, 1)), new[] { position, other }, Array.Empty<GuidanceAssessment>(), 100);
        Assert.True(result.UnallocatedAmount > 0);
        Assert.True(result.Allocations.Single().ProjectedAssetWeight <= 15m);
        Assert.Equal(result.ContributionAmount, result.Allocations.Sum(x => x.Amount) + result.UnallocatedAmount);
    }

    [Fact]
    public void Sem_CapitalNeeds_retorna_NoAllocation_com_valor_integralmente_nao_alocado()
    {
        var result = Calculate(Strategy(ContributionPolicy.MaintainTargetAllocation), Health(), new[] { new GuidancePosition(Guid.NewGuid(), "OK3", AssetClass.Fii, 100, 100) }, Array.Empty<GuidanceAssessment>(), 100);
        Assert.Equal(ContributionAllocationStyle.NoAllocation, result.AllocationStyle);
        Assert.Equal(100, result.UnallocatedAmount);
    }

    [Fact]
    public void Guidance_nao_muta_posicoes_e_empate_e_deterministico()
    {
        var a = new GuidancePosition(Guid.NewGuid(), "A3", AssetClass.Fii, 100, 10); var b = new GuidancePosition(Guid.NewGuid(), "B3", AssetClass.Fii, 100, 10);
        var input = Health(new CapitalNeed(AssetClass.Fii, 10, 50, 40, 1)); var before = a.CurrentValue;
        var first = Calculate(Strategy(ContributionPolicy.MaintainTargetAllocation), input, new[] { b, a }, Array.Empty<GuidanceAssessment>(), 10);
        var second = Calculate(Strategy(ContributionPolicy.MaintainTargetAllocation), input, new[] { b, a }, Array.Empty<GuidanceAssessment>(), 10);
        Assert.Equal(first.Allocations.Select(x => x.AssetId), second.Allocations.Select(x => x.AssetId));
        Assert.Equal(before, a.CurrentValue);
    }

    [Fact]
    public void Capacidade_usa_somente_capital_alocado_no_denominador()
    {
        var asset = new GuidancePosition(Guid.NewGuid(), "LIMIT15", AssetClass.Fii, 14000, 14);
        var other = new GuidancePosition(Guid.NewGuid(), "OTHER15", AssetClass.BrazilianStock, 86000, 86);
        var result = Calculate(Strategy(ContributionPolicy.CorrectImbalancesFirst, 15), Health(new CapitalNeed(AssetClass.Fii, 14, 40, 26, 1)), new[] { asset, other }, Array.Empty<GuidanceAssessment>(), 5000);

        var allocation = Assert.Single(result.Allocations);
        Assert.Equal(1176.47m, allocation.Amount);
        Assert.Equal(3823.53m, result.UnallocatedAmount);
        Assert.Equal(15m, allocation.ProjectedAssetWeight);
        Assert.Equal(result.ContributionAmount, result.Allocations.Sum(x => x.Amount) + result.UnallocatedAmount);
    }

    [Fact]
    public void Allocacoes_distribuidas_usam_soma_das_allocations_no_denominador()
    {
        var a = new GuidancePosition(Guid.NewGuid(), "A15", AssetClass.Fii, 10000, 10);
        var b = new GuidancePosition(Guid.NewGuid(), "B15", AssetClass.Fii, 10000, 10);
        var other = new GuidancePosition(Guid.NewGuid(), "OTHER15B", AssetClass.BrazilianStock, 80000, 80);
        var result = Calculate(Strategy(ContributionPolicy.CorrectImbalancesFirst, 15), Health(new CapitalNeed(AssetClass.Fii, 20, 50, 30, 1)), new[] { a, b, other }, Array.Empty<GuidanceAssessment>(), 10000);

        Assert.Equal(2, result.Allocations.Count);
        Assert.Equal(0, result.UnallocatedAmount);
        Assert.Equal(result.ContributionAmount, result.Allocations.Sum(x => x.Amount) + result.UnallocatedAmount);
        Assert.All(result.Allocations, x => Assert.True(x.ProjectedAssetWeight <= 15m));
    }

    [Fact]
    public void Maximum_nulo_nao_aplica_limite_artificial()
    {
        var asset = new GuidancePosition(Guid.NewGuid(), "NOMAX", AssetClass.Fii, 14000, 14);
        var result = Calculate(Strategy(ContributionPolicy.CorrectImbalancesFirst), Health(new CapitalNeed(AssetClass.Fii, 14, 40, 26, 1)), new[] { asset }, Array.Empty<GuidanceAssessment>(), 5000);
        Assert.Equal(5000m, result.Allocations.Single().Amount);
        Assert.Equal(0m, result.UnallocatedAmount);
    }

    private static ContributionGuidance Calculate(PortfolioAllocationStrategy strategy, PortfolioHealthResult health, IReadOnlyList<GuidancePosition> positions, IReadOnlyList<GuidanceAssessment> assessments, decimal amount)
        => new ContributionGuidanceCalculator().Calculate(new ContributionGuidanceInput(Guid.NewGuid(), amount, strategy, health, positions, assessments));
    private static PortfolioAllocationStrategy Strategy(ContributionPolicy policy, decimal? max = null) => new(Guid.NewGuid(), "R4", null, 1, PriorityLevel.Medium, PriorityLevel.Medium, PriorityLevel.Medium, policy, max);
    private static PortfolioHealthResult Health(params CapitalNeed[] needs) => new(PortfolioHealthStatus.Aligned, Array.Empty<AllocationDeviation>(), null, needs, Array.Empty<Concentration>(), "1", 1, DateTimeOffset.UtcNow);
}
