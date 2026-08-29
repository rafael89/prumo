using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Domain.Rules;
using Lodira.Application.Portfolio;
using Lodira.Application.Radar;
using Lodira.Application.Abstractions;
using Lodira.Application.Intelligence;
using Lodira.Api.Services;
using Lodira.Infrastructure.Persistence;

namespace Lodira.Api.Controllers;

[ApiController]
[Route("api/portfolios")]
public sealed class PortfoliosController : ControllerBase
{
    private readonly LodiraDbContext dbContext;
    private readonly PortfolioHealthCalculator portfolioHealthCalculator = new();
    private readonly IPortfolioAssessmentFreshnessEvaluator freshnessEvaluator = new PortfolioAssessmentFreshnessEvaluator();
    private readonly PortfolioAssessmentCurrentService currentAssessmentService;

    public PortfoliosController(LodiraDbContext dbContext, PortfolioAssessmentCurrentService? currentAssessmentService = null)
    {
        this.dbContext = dbContext;
        var evaluator = new PortfolioAssessmentFreshnessEvaluator();
        this.currentAssessmentService = currentAssessmentService ?? new PortfolioAssessmentCurrentService(dbContext, new PortfolioImpactAssessmentService(), evaluator, Microsoft.Extensions.Logging.Abstractions.NullLogger<PortfolioAssessmentCurrentService>.Instance);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PortfolioSummaryResponse>>> GetPortfolios(CancellationToken cancellationToken)
    {
        var portfolios = await dbContext.Portfolios
            .AsNoTracking()
            .Include(portfolio => portfolio.Positions)
            .ToListAsync(cancellationToken);

        return Ok(portfolios.Select(CreateSummary).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<PortfolioSummaryResponse>> CreatePortfolio(
        CreatePortfolioRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("O nome da carteira é obrigatório.");
        }

        var portfolio = new Portfolio(request.Name);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetPortfolio), new { portfolioId = portfolio.Id }, CreateSummary(portfolio));
    }

    [HttpGet("{portfolioId:guid}")]
    public async Task<ActionResult<PortfolioDetailResponse>> GetPortfolio(
        Guid portfolioId,
        CancellationToken cancellationToken)
    {
        var portfolio = await LoadPortfolio(portfolioId, cancellationToken);
        return portfolio is null ? NotFound() : Ok(CreateDetail(portfolio));
    }

    [HttpPut("{portfolioId:guid}")]
    public async Task<IActionResult> RenamePortfolio(
        Guid portfolioId,
        RenamePortfolioRequest request,
        CancellationToken cancellationToken)
    {
        var portfolio = await dbContext.Portfolios.FindAsync(new object[] { portfolioId }, cancellationToken);
        if (portfolio is null) return NotFound();

        try
        {
            portfolio.Rename(request.Name);
            await dbContext.SaveChangesAsync(cancellationToken);
            return NoContent();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpDelete("{portfolioId:guid}")]
    public async Task<IActionResult> DeletePortfolio(Guid portfolioId, CancellationToken cancellationToken)
    {
        var portfolio = await dbContext.Portfolios.FindAsync(new object[] { portfolioId }, cancellationToken);
        if (portfolio is null) return NotFound();

        dbContext.Portfolios.Remove(portfolio);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("{portfolioId:guid}/dashboard")]
    public async Task<ActionResult<DashboardResponse>> GetDashboard(Guid portfolioId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 10, 100);
        var portfolio = await dbContext.Portfolios.AsNoTracking().SingleOrDefaultAsync(item => item.Id == portfolioId, cancellationToken);
        if (portfolio is null) return NotFound();
        var positionsQuery = dbContext.PortfolioPositions.AsNoTracking().Where(position => position.PortfolioId == portfolioId);
        var positionCount = await positionsQuery.CountAsync(cancellationToken);
        var totalValue = await positionsQuery.SumAsync(position => position.CurrentPrice.HasValue ? position.Quantity * position.CurrentPrice.Value : 0m, cancellationToken);
        var positions = await positionsQuery.Include(position => position.Asset).OrderBy(position => position.Asset!.Ticker).ThenBy(position => position.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var strategy = await dbContext.PortfolioAllocationStrategies.AsNoTracking().Include(s => s.Allocations).SingleOrDefaultAsync(s => s.PortfolioId == portfolioId && s.ArchivedAt == null, cancellationToken);
        var health = await LoadHealthForDashboard(portfolioId, strategy, cancellationToken);
        var objective = await dbContext.InvestmentObjectives.AsNoTracking().SingleOrDefaultAsync(o => o.PortfolioId == portfolioId && o.ArchivedAt == null, cancellationToken);
        var snapshotPortfolio = await dbContext.Portfolios.AsNoTracking().Include(p => p.Positions).ThenInclude(p => p.Asset).SingleAsync(p => p.Id == portfolioId, cancellationToken);
        var snapshot = PortfolioSnapshotKeyCalculator.Calculate(snapshotPortfolio);
        var loadedAssessments = await dbContext.PortfolioAssessments.AsNoTracking().Include(a => a.InvestmentEvent!).ThenInclude(e => e.AffectedAssets).Where(a => a.PortfolioId == portfolioId && a.Status != AssessmentStatus.Closed).OrderByDescending(a => a.GeneratedAt).ThenByDescending(a => a.Id).ToListAsync(cancellationToken);
        var assessments = (await currentAssessmentService.EnsureFreshAsync(snapshotPortfolio, strategy, objective, loadedAssessments, cancellationToken)).OrderByDescending(a => a.GeneratedAt).ThenByDescending(a => a.Id).ToList();
        var attention = assessments.Where(RadarAttentionNowPolicy.RequiresAttention).Take(3).Select(CreateDashboardAssessment).ToList();
        var attentionIds = attention.Select(x => x.AssessmentId).ToHashSet();
        var recent = assessments.Where(a => !attentionIds.Contains(a.Id)).Take(3).Select(CreateDashboardAssessment).ToList();
        var primaryNeed = health.CapitalNeeds.OrderBy(x => x.PriorityOrder).FirstOrDefault();
        return Ok(new DashboardResponse(
            portfolio.Id,
            portfolio.Name,
            totalValue,
            positionCount,
            positions.Select(CreatePositionResponse).ToList(), page, pageSize, positionCount, (int)Math.Ceiling(positionCount / (double)pageSize), DashboardState.Ready,
            new PortfolioOverview(portfolio.Id, portfolio.Name, totalValue, health.Status.ToString(), HealthLabel(health.Status), health.MainDeviation is null ? null : DeviationLabel(health.MainDeviation), await positionsQuery.MaxAsync(p => (DateTimeOffset?)p.UpdatedAt, cancellationToken)), attention,
            new ContributionPreview(strategy is null ? ContributionPreviewStatus.NotConfigured : health.CapitalNeeds.Count == 0 ? ContributionPreviewStatus.NoNeeds : ContributionPreviewStatus.Available, primaryNeed?.AssetClass, primaryNeed is null ? strategy is null ? "Defina sua estratégia para receber orientação de aporte." : "Sua carteira está alinhada à estratégia atual; nenhuma classe precisa ser priorizada neste momento." : $"{AssetClassLabel(primaryNeed.AssetClass)} são hoje a principal necessidade da carteira."),
            new PortfolioHealthSummary(health.Status.ToString(), HealthLabel(health.Status), health.AllocationDeviations, health.MainDeviation, health.Concentrations), recent, DateTimeOffset.UtcNow));
    }

    private async Task<PortfolioHealthResult> LoadHealthForDashboard(Guid portfolioId, PortfolioAllocationStrategy? strategy, CancellationToken cancellationToken)
    {
        var portfolio = await dbContext.Portfolios.Include(p => p.Positions).ThenInclude(p => p.Asset).SingleAsync(p => p.Id == portfolioId, cancellationToken);
        return portfolioHealthCalculator.Calculate(portfolio, strategy);
    }

    private static DashboardAssessment CreateDashboardAssessment(PortfolioAssessment assessment)
        => new(assessment.Id, assessment.InvestmentEventId, assessment.InvestmentEvent?.AffectedAssets.FirstOrDefault()?.Ticker ?? "Carteira", assessment.InvestmentEvent?.Summary ?? assessment.Summary, assessment.WhyItMatters, assessment.SuggestedBehavior, assessment.InvestmentEvent?.OccurredAt);

    private static string HealthLabel(PortfolioHealthStatus status) => status switch
    {
        PortfolioHealthStatus.Aligned => "Carteira alinhada à estratégia.", PortfolioHealthStatus.SlightlyUnbalanced => "Carteira levemente fora da estratégia.", PortfolioHealthStatus.Unbalanced => "Carteira fora da estratégia.", PortfolioHealthStatus.StronglyUnbalanced => "Carteira significativamente fora da estratégia.", PortfolioHealthStatus.NotConfigured => "Estratégia ainda não configurada.", PortfolioHealthStatus.EmptyPortfolio => "Carteira ainda sem posições.", _ => "Estado da carteira indisponível."
    };
    private static string DeviationLabel(MainDeviation deviation) => $"{AssetClassLabel(deviation.AssetClass)} estão {Math.Abs(deviation.DeviationPercentagePoints):0.##} p.p. {(deviation.Direction == AllocationDeviationDirection.Underweight ? "abaixo" : "acima")} da alocação definida.";
    private static string AssetClassLabel(AssetClass assetClass) => assetClass switch { AssetClass.Fii => "FIIs", AssetClass.BrazilianStock => "Ações", AssetClass.FixedIncome => "Renda fixa", AssetClass.International => "Ativos internacionais", _ => "Outros ativos" };

    [HttpGet("{portfolioId:guid}/health")]
    public async Task<ActionResult<PortfolioHealthResult>> GetHealth(Guid portfolioId, CancellationToken cancellationToken)
    {
        var portfolio = await dbContext.Portfolios.Include(p => p.Positions).ThenInclude(p => p.Asset).SingleOrDefaultAsync(p => p.Id == portfolioId, cancellationToken);
        if (portfolio is null) return NotFound();
        var strategy = await dbContext.PortfolioAllocationStrategies.Include(s => s.Allocations)
            .SingleOrDefaultAsync(s => s.PortfolioId == portfolioId && s.ArchivedAt == null, cancellationToken);
        return Ok(portfolioHealthCalculator.Calculate(portfolio, strategy));
    }

    [HttpPost("{portfolioId:guid}/positions")]
    public async Task<ActionResult<PositionResponse>> AddPosition(
        Guid portfolioId,
        PositionRequest request,
        CancellationToken cancellationToken)
    {
        var portfolio = await LoadPortfolio(portfolioId, cancellationToken);
        if (portfolio is null) return NotFound("Carteira não encontrada.");

        if (request.Quantity < 0 || request.AveragePrice < 0 || request.CurrentPrice is < 0)
        {
            return BadRequest("Quantidade, preço médio e cotação não podem ser negativos.");
        }

        var ticker = request.Ticker.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(ticker) || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Ticker e nome do ativo são obrigatórios.");
        }

        if (portfolio.Positions.Any(position => position.Asset?.Ticker == ticker))
        {
            return Conflict("O ativo já existe nesta carteira.");
        }

        var asset = await dbContext.Assets.FirstOrDefaultAsync(item => item.Ticker == ticker, cancellationToken);
        if (asset is null)
        {
            asset = new Asset(ticker, request.Name, request.AssetType, request.Sector, request.AssetClass);
            dbContext.Assets.Add(asset);
        }

        var position = new PortfolioPosition(portfolio.Id, asset.Id, request.Quantity, request.AveragePrice, request.CurrentPrice);
        dbContext.PortfolioPositions.Add(position);
        portfolio.Positions.Add(position);
        Recalculate(portfolio);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetPortfolio), new { portfolioId }, CreatePositionResponse(position));
    }

    [HttpPut("{portfolioId:guid}/positions/{positionId:guid}")]
    public async Task<ActionResult<PositionResponse>> UpdatePosition(
        Guid portfolioId,
        Guid positionId,
        PositionUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var portfolio = await LoadPortfolio(portfolioId, cancellationToken);
        var position = portfolio?.Positions.SingleOrDefault(item => item.Id == positionId);
        if (position is null) return NotFound();

        try
        {
            position.Update(request.Quantity, request.AveragePrice, request.CurrentPrice);
            Recalculate(portfolio!);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Ok(CreatePositionResponse(position));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return BadRequest($"{exception.ParamName} não pode ser negativo.");
        }
    }

    [HttpDelete("{portfolioId:guid}/positions/{positionId:guid}")]
    public async Task<IActionResult> DeletePosition(Guid portfolioId, Guid positionId, CancellationToken cancellationToken)
    {
        var portfolio = await LoadPortfolio(portfolioId, cancellationToken);
        var position = portfolio?.Positions.SingleOrDefault(item => item.Id == positionId);
        if (position is null) return NotFound();

        dbContext.PortfolioPositions.Remove(position);
        portfolio!.Positions.Remove(position);
        Recalculate(portfolio);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<Portfolio?> LoadPortfolio(Guid portfolioId, CancellationToken cancellationToken)
    {
        return await dbContext.Portfolios
            .Include(portfolio => portfolio.Positions)
            .ThenInclude(position => position.Asset)
            .SingleOrDefaultAsync(portfolio => portfolio.Id == portfolioId, cancellationToken);
    }

    private static void Recalculate(Portfolio portfolio)
    {
        PortfolioCalculator.RecalculateWeights(portfolio.Positions);
    }

    private static PortfolioSummaryResponse CreateSummary(Portfolio portfolio)
    {
        return new PortfolioSummaryResponse(
            portfolio.Id,
            portfolio.Name,
            PortfolioCalculator.CalculateTotalValue(portfolio.Positions),
            portfolio.Positions.Count);
    }

    private static PortfolioDetailResponse CreateDetail(Portfolio portfolio)
    {
        return new PortfolioDetailResponse(
            portfolio.Id,
            portfolio.Name,
            PortfolioCalculator.CalculateTotalValue(portfolio.Positions),
            portfolio.Positions.Select(CreatePositionResponse).ToList());
    }

    private static PositionResponse CreatePositionResponse(PortfolioPosition position)
    {
        return new PositionResponse(
            position.Id,
            position.AssetId,
            position.Asset?.AssetClass ?? AssetClass.Other,
            position.Asset?.Ticker ?? string.Empty,
            position.Asset?.Name ?? string.Empty,
            position.Asset?.AssetType ?? AssetType.Other,
            position.Asset?.Sector,
            position.Quantity,
            position.AveragePrice,
            position.CurrentPrice,
            position.CurrentValue,
            position.PortfolioWeight);
    }
}

public sealed record CreatePortfolioRequest(string Name);
public sealed record RenamePortfolioRequest(string Name);
public sealed record PositionRequest(string Ticker, string Name, AssetType AssetType, string? Sector, decimal Quantity, decimal AveragePrice, decimal? CurrentPrice, AssetClass? AssetClass = null);
public sealed record PositionUpdateRequest(decimal Quantity, decimal AveragePrice, decimal? CurrentPrice);
public sealed record PortfolioSummaryResponse(Guid Id, string Name, decimal TotalValue, int PositionCount);
public sealed record PortfolioDetailResponse(Guid Id, string Name, decimal TotalValue, IReadOnlyList<PositionResponse> Positions);
public enum DashboardState { Ready, FirstUse, Processing, StrategyNotConfigured }
public enum ContributionPreviewStatus { Available, NotConfigured, NoNeeds, EmptyPortfolio }
public sealed record PortfolioOverview(Guid PortfolioId, string PortfolioName, decimal CurrentValue, string HealthStatus, string HealthSummary, string? MainDeviationSummary, DateTimeOffset? LastPortfolioUpdate);
public sealed record ContributionPreview(ContributionPreviewStatus Status, AssetClass? PrimaryCapitalNeed, string Summary);
public sealed record DashboardAssessment(Guid AssessmentId, Guid InvestmentEventId, string Asset, string EventSummary, string WhyItMatters, string SuggestedBehavior, DateTimeOffset? OccurredAt);
public sealed record PortfolioHealthSummary(string Status, string Summary, IReadOnlyList<AllocationDeviation> AllocationDeviations, MainDeviation? MainDeviation, IReadOnlyList<Concentration> Concentrations);
public sealed record DashboardResponse(Guid Id, string Name, decimal TotalValue, int PositionCount, IReadOnlyList<PositionResponse> Positions, int Page = 1, int PageSize = 10, int TotalItems = 0, int TotalPages = 0, DashboardState State = DashboardState.Ready, PortfolioOverview? PortfolioOverview = null, IReadOnlyList<DashboardAssessment>? AttentionNow = null, ContributionPreview? ContributionPreview = null, PortfolioHealthSummary? PortfolioHealthSummary = null, IReadOnlyList<DashboardAssessment>? RecentUpdates = null, DateTimeOffset? GeneratedAt = null);
public sealed record PositionResponse(Guid Id, Guid AssetId, AssetClass AssetClass, string Ticker, string Name, AssetType AssetType, string? Sector, decimal Quantity, decimal AveragePrice, decimal? CurrentPrice, decimal? CurrentValue, decimal PortfolioWeight);
