using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lodira.Api.Controllers;
using Lodira.Application.Radar;
using Lodira.Application.Intelligence;
using Lodira.Application.Portfolio;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Infrastructure.Persistence;

namespace Lodira.Domain.Tests;

public sealed class DashboardCompositionTests
{
    [Fact]
    public async Task Dashboard_configurado_e_saudavel_compõe_health_e_preview()
    {
        await using var db = Context(); var portfolio = SeedPortfolio(db, true); await db.SaveChangesAsync();
        var result = await Dashboard(db, portfolio.Id); var model = Ok(result);
        Assert.Equal(PortfolioHealthStatus.Aligned.ToString(), model.PortfolioOverview!.HealthStatus);
        Assert.Equal(ContributionPreviewStatus.NoNeeds, model.ContributionPreview!.Status);
        Assert.Null(model.ContributionPreview.PrimaryCapitalNeed);
    }

    [Fact]
    public async Task Dashboard_slightly_unbalanced_usa_CapitalNeeds_como_preview()
    {
        await using var db = Context(); var portfolio = SeedPortfolio(db, true); var second = new Asset("STK2", "Ação 2", AssetType.Stock, null); db.Assets.Add(second); db.PortfolioPositions.Add(new PortfolioPosition(portfolio.Id, second.Id, 1, 100, 100)); await db.SaveChangesAsync();
        var result = Ok(await Dashboard(db, portfolio.Id));
        Assert.Equal(ContributionPreviewStatus.Available, result.ContributionPreview!.Status);
        Assert.Equal(AssetClass.Fii, result.ContributionPreview.PrimaryCapitalNeed);
        Assert.Contains("FIIs", result.ContributionPreview.Summary);
    }

    [Fact]
    public async Task Dashboard_sem_strategy_e_sem_posicoes_expoe_estados_sem_inventar_dados()
    {
        await using var db = Context(); var empty = new Portfolio("Vazia"); db.Portfolios.Add(empty); await db.SaveChangesAsync();
        var result = Ok(await Dashboard(db, empty.Id));
        Assert.Equal(PortfolioHealthStatus.NotConfigured.ToString(), result.PortfolioOverview!.HealthStatus);
        Assert.Equal(ContributionPreviewStatus.NotConfigured, result.ContributionPreview!.Status);
        Assert.Empty(result.AttentionNow!); Assert.Empty(result.RecentUpdates!);
    }

    [Fact]
    public async Task Dashboard_com_atencao_e_recent_updates_limita_e_separa_as_listas()
    {
        await using var db = Context(); var portfolio = SeedPortfolio(db, true); var asset = portfolio.Positions[0].Asset!;
        for (var i = 0; i < 5; i++)
        {
            var ev = new InvestmentEvent(Guid.NewGuid(), InvestmentEventType.CorporateAction, DateTimeOffset.UtcNow.AddMinutes(-i), $"Evento {i}", new[] { "Fato" }); ev.AffectedAssets.Add(asset); db.InvestmentEvents.Add(ev); db.PortfolioAssessments.Add(new PortfolioAssessment(portfolio.Id, ev.Id, i < 4 ? AttentionPriority.High : AttentionPriority.Low, i < 4 ? ActionRequirement.Review : ActionRequirement.None, CapitalDecisionImpact.None, AssessmentStatus.Active, AssessmentUserState.Unread, ev.Summary, "Importa", "Revisar", IntelligenceConfidence.High, 1, null, PortfolioImpactAssessmentService.CurrentVersion, PortfolioSnapshotKeyCalculator.Calculate(portfolio), ev.Version));
        }
        await db.SaveChangesAsync(); var result = Ok(await Dashboard(db, portfolio.Id));
        Assert.Equal(3, result.AttentionNow!.Count); Assert.InRange(result.RecentUpdates!.Count, 1, 3); Assert.DoesNotContain(result.RecentUpdates!, x => result.AttentionNow!.Any(a => a.AssessmentId == x.AssessmentId));
    }

    [Fact]
    public async Task Dashboard_closed_nao_entra_em_AttentionNow_e_Favor_nao_cria_preview()
    {
        await using var db = Context(); var portfolio = SeedPortfolio(db, true); var asset = portfolio.Positions[0].Asset!; var ev = new InvestmentEvent(Guid.NewGuid(), InvestmentEventType.Other, null, "Fechado", new[] { "Fato" }); ev.AffectedAssets.Add(asset); db.InvestmentEvents.Add(ev); db.PortfolioAssessments.Add(new PortfolioAssessment(portfolio.Id, ev.Id, AttentionPriority.Critical, ActionRequirement.Act, CapitalDecisionImpact.Favor, AssessmentStatus.Closed, AssessmentUserState.Unread, "Resumo", "Importa", "Acompanhar", IntelligenceConfidence.High, 1, 1)); await db.SaveChangesAsync();
        var result = Ok(await Dashboard(db, portfolio.Id)); Assert.Empty(result.AttentionNow!); Assert.Equal(ContributionPreviewStatus.NoNeeds, result.ContributionPreview!.Status);
    }

    [Fact]
    public async Task Dashboard_registra_ultima_atualizacao_das_posicoes()
    {
        await using var db = Context(); var portfolio = SeedPortfolio(db, true); await db.SaveChangesAsync(); var expected = portfolio.Positions[0].UpdatedAt;
        var result = Ok(await Dashboard(db, portfolio.Id)); Assert.Equal(expected, result.PortfolioOverview!.LastPortfolioUpdate);
    }

    [Fact]
    public async Task Dashboard_inexistente_retorna_NotFound()
    {
        await using var db = Context(); var result = await Dashboard(db, Guid.NewGuid()); Assert.IsType<NotFoundResult>(result.Result);
    }

    private static async Task<ActionResult<DashboardResponse>> Dashboard(LodiraDbContext db, Guid id) => await new PortfoliosController(db).GetDashboard(id);
    private static DashboardResponse Ok(ActionResult<DashboardResponse> result) => Assert.IsType<OkObjectResult>(result.Result).Value as DashboardResponse ?? throw new Xunit.Sdk.XunitException("Dashboard response ausente");
    private static LodiraDbContext Context() => new(new DbContextOptionsBuilder<LodiraDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static Portfolio SeedPortfolio(LodiraDbContext db, bool strategy)
    {
        var portfolio = new Portfolio("Carteira"); var asset = new Asset("FII1", "FII 1", AssetType.Fii, null); db.Portfolios.Add(portfolio); db.Assets.Add(asset); portfolio.Positions.Add(new PortfolioPosition(portfolio.Id, asset.Id, 10, 100, 100)); db.PortfolioPositions.Add(portfolio.Positions[0]);
        if (strategy) { var item = new PortfolioAllocationStrategy(portfolio.Id, "Estratégia", null, 1, PriorityLevel.Medium, PriorityLevel.Medium, PriorityLevel.Medium, ContributionPolicy.CorrectImbalancesFirst, null); db.PortfolioAllocationStrategies.Add(item); db.StrategyAllocations.Add(new StrategyAllocation(item.Id, AssetClass.Fii, 100)); }
        return portfolio;
    }
}
