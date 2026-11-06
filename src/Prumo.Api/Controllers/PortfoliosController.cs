using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Domain.Rules;
using Prumo.Application.Portfolio;
using Prumo.Application.Radar;
using Prumo.Application.Abstractions;
using Prumo.Application.Intelligence;
using Prumo.Api.Services;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.PortfolioImports;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/portfolios")]
public sealed class PortfoliosController : ControllerBase
{
    private readonly PrumoDbContext dbContext;
    private readonly PortfolioHealthCalculator portfolioHealthCalculator = new();
    private readonly IPortfolioAssessmentFreshnessEvaluator freshnessEvaluator = new PortfolioAssessmentFreshnessEvaluator();
    private readonly PortfolioAssessmentCurrentService currentAssessmentService;
    private readonly NewAssetBootstrapService? documentBootstrap;
    private readonly ILogger<PortfoliosController>? logger;

    public PortfoliosController(PrumoDbContext dbContext, PortfolioAssessmentCurrentService? currentAssessmentService = null, NewAssetBootstrapService? documentBootstrap = null, ILogger<PortfoliosController>? logger = null)
    {
        this.dbContext = dbContext;
        var evaluator = new PortfolioAssessmentFreshnessEvaluator();
        this.currentAssessmentService = currentAssessmentService ?? new PortfolioAssessmentCurrentService(dbContext, new PortfolioImpactAssessmentService(), evaluator, Microsoft.Extensions.Logging.Abstractions.NullLogger<PortfolioAssessmentCurrentService>.Instance);
        this.documentBootstrap = documentBootstrap;
        this.logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PortfolioSummaryResponse>>> GetPortfolios(CancellationToken cancellationToken)
    {
        var portfolios = await dbContext.Portfolios.AsNoTracking()
            .Select(p => new PortfolioSummaryResponse(p.Id, p.Name, p.Positions.Sum(x => x.CurrentValue ?? 0m), p.Positions.Count))
            .ToListAsync(cancellationToken);
        return Ok(portfolios);
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
        if (portfolio is not null) PortfolioCalculator.RecalculateWeights(portfolio.Positions);
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
        var portfolio = await dbContext.Portfolios.AsNoTracking().Include(p => p.Positions).ThenInclude(p => p.Asset).SingleOrDefaultAsync(item => item.Id == portfolioId, cancellationToken);
        if (portfolio is null) return NotFound();
        var allPositions = portfolio.Positions.OrderBy(position => position.Asset?.Ticker ?? string.Empty).ThenBy(position => position.Id).ToList();
        PortfolioCalculator.RecalculateWeights(allPositions);
        var positionCount = allPositions.Count;
        var totalValue = allPositions.Sum(position => position.CurrentValue ?? 0m);
        var positions = allPositions.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var targets = await dbContext.TargetAllocations.AsNoTracking().Where(x => x.PortfolioId == portfolioId).ToListAsync(cancellationToken);
        var health = portfolioHealthCalculator.Calculate(portfolio, targets);
        var reader = new PortfolioAssessmentReader(dbContext, currentAssessmentService, freshnessEvaluator);
        var assessmentQuery = dbContext.PortfolioAssessments.AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId && x.InvestmentEvent!.DocumentAnalysis!.Document != null);
        var rows = (await reader.ReadAsync(assessmentQuery, portfolio, targets, cancellationToken))
            .Where(x => x.Assessment.Status != AssessmentStatus.Closed).ToList();
        var rowsById = rows.ToDictionary(x => x.Assessment.Id);
        var representatives = rows.GroupBy(x => RadarEventPolicy.CardId(x.Event))
            .Select(group => RadarAssessmentSelector.SelectRepresentative(group.Select(x => x.Assessment))).ToList();
        var attentionRows = representatives
            .Where(a => RadarEventPolicy.Importance(a.InvestmentEvent!, rowsById[a.Id].Importance) is AnalysisImportance.Critical or AnalysisImportance.Relevant)
            .Where(RadarAttentionNowPolicy.RequiresAttention)
            .OrderByDescending(a => rowsById[a.Id].PublishedAt)
            .ThenByDescending(a => a.AttentionPriority).ThenByDescending(a => a.InvestmentEvent!.OccurredAt)
            .ThenByDescending(a => a.GeneratedAt).ThenByDescending(a => a.Id).ToList();
        var portfolioAssetIds = allPositions.Select(position => position.AssetId).ToHashSet();
        var newsQuery = LatestAnalyses(portfolioAssetIds);
        var recentTotal = await newsQuery.CountAsync(cancellationToken);
        var analyzedDocuments = await newsQuery.Take(5).ToListAsync(cancellationToken);
        var newsIds = analyzedDocuments.Select(x => x.Id).ToHashSet();
        var requiredCards = attentionRows.Select(x => RadarEventPolicy.CardId(x.InvestmentEvent!))
            .Concat(rows.Where(x => newsIds.Contains(x.Event.DocumentAnalysisId)).Select(x => RadarEventPolicy.CardId(x.Event))).ToHashSet();
        var detailIds = rows.Where(x => requiredCards.Contains(RadarEventPolicy.CardId(x.Event))).Select(x => x.Assessment.Id).ToArray();
        var details = await reader.LoadDetailsAsync(detailIds, cancellationToken);
        var legacyLiquidations = rows.Where(x => x.LegacyLiquidation).Select(x => x.Assessment.Id).ToHashSet();
        foreach (var detail in details.Where(x => legacyLiquidations.Contains(x.Id))) detail.OverrideCapitalDecisionImpact(CapitalDecisionImpact.Block);
        var detailsById = details.ToDictionary(x => x.Id);
        var eventsByDocument = details.GroupBy(x => RadarEventPolicy.CardId(x.InvestmentEvent!)).ToDictionary(g => g.Key, BuildDashboardEvents);
        var attention = attentionRows.Select(a => CreateDashboardAssessment(detailsById[a.Id], eventsByDocument.GetValueOrDefault(RadarEventPolicy.CardId(a.InvestmentEvent!)))).ToList();
        var recent = BuildNews(analyzedDocuments, details);
        var primaryNeed = health.CapitalNeeds.OrderBy(x => x.PriorityOrder).FirstOrDefault();
        return Ok(new DashboardResponse(
            portfolio.Id,
            portfolio.Name,
            totalValue,
            positionCount,
            positions.Select(CreatePositionResponse).ToList(), page, pageSize, positionCount, (int)Math.Ceiling(positionCount / (double)pageSize), DashboardState.Ready,
            new PortfolioOverview(portfolio.Id, portfolio.Name, totalValue, health.Status.ToString(), HealthLabel(health.Status), health.MainDeviation is null ? null : DeviationLabel(health.MainDeviation), allPositions.Select(p => (DateTimeOffset?)p.UpdatedAt).DefaultIfEmpty().Max()), attention,
            new ContributionPreview(targets.Count == 0 ? ContributionPreviewStatus.NotConfigured : health.CapitalNeeds.Count == 0 ? ContributionPreviewStatus.NoNeeds : ContributionPreviewStatus.Available, primaryNeed?.AssetClass, primaryNeed is null ? targets.Count == 0 ? "Defina as alocações-alvo para receber orientação de aporte." : "Sua carteira está alinhada às alocações-alvo; nenhuma classe precisa ser priorizada neste momento." : $"{AssetClassLabel(primaryNeed.AssetClass)} são hoje a principal necessidade da carteira."),
            new PortfolioHealthSummary(health.Status.ToString(), HealthLabel(health.Status), health.AllocationDeviations, health.MainDeviation), recent, DateTimeOffset.UtcNow, recentTotal));
    }

    [HttpGet("{portfolioId:guid}/news")]
    public async Task<ActionResult<Prumo.Api.Models.PaginatedResponse<DashboardAssessment>>> GetNews(Guid portfolioId, [FromQuery] int page = 1, [FromQuery] int pageSize = 5, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var portfolio = await dbContext.Portfolios.AsNoTracking().Include(p => p.Positions).ThenInclude(p => p.Asset).SingleOrDefaultAsync(p => p.Id == portfolioId, cancellationToken);
        if (portfolio is null) return NotFound();
        var ids = portfolio.Positions.Select(x => x.AssetId).ToHashSet();
        var query = LatestAnalyses(ids);
        var total = await query.CountAsync(cancellationToken);
        var analyses = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var analysisIds = analyses.Select(x => x.Id).ToArray();
        var assessments = await dbContext.PortfolioAssessments.AsNoTracking().AsSplitQuery()
            .Include(a => a.InvestmentEvent!).ThenInclude(e => e.AffectedAssets)
            .Include(a => a.InvestmentEvent!).ThenInclude(e => e.Evidence)
            .Include(a => a.InvestmentEvent!).ThenInclude(e => e.DocumentAnalysis!).ThenInclude(a => a.Document)
            .Where(a => a.PortfolioId == portfolioId && analysisIds.Contains(a.InvestmentEvent!.DocumentAnalysisId)).ToListAsync(cancellationToken);
        PortfolioCalculator.RecalculateWeights(portfolio.Positions);
        var targets = await dbContext.TargetAllocations.AsNoTracking().Where(x => x.PortfolioId == portfolioId).ToListAsync(cancellationToken);
        var refreshed = await currentAssessmentService.EnsureFreshAsync(portfolio, targets, assessments, cancellationToken);
        return Ok(new Prumo.Api.Models.PaginatedResponse<DashboardAssessment>(BuildNews(analyses, refreshed), page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize)));
    }

    private IOrderedQueryable<DocumentAnalysis> LatestAnalyses(HashSet<Guid> assetIds)
        => dbContext.DocumentAnalyses.AsNoTracking().Include(a => a.Document!).ThenInclude(d => d.Asset)
            .Where(a => assetIds.Contains(a.Document!.AssetId))
            .Where(a => !dbContext.DocumentAnalyses.Any(other => other.DocumentId == a.DocumentId
                && (other.CreatedAt > a.CreatedAt || other.CreatedAt == a.CreatedAt && other.Id > a.Id)))
            .OrderByDescending(a => a.Document!.PublishedAt).ThenByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id);

    private static List<DashboardAssessment> BuildNews(IReadOnlyList<DocumentAnalysis> analyses, IReadOnlyList<PortfolioAssessment> assessments)
    {
        var byAnalysis = assessments.Where(a => a.InvestmentEvent is not null).GroupBy(a => RadarEventPolicy.CardId(a.InvestmentEvent!))
            .Select(RadarAssessmentSelector.SelectRepresentative).GroupBy(a => a.InvestmentEvent!.DocumentAnalysisId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.GeneratedAt).ThenByDescending(a => a.Id).First());
        var events = assessments.Where(a => a.InvestmentEvent is not null).GroupBy(a => RadarEventPolicy.CardId(a.InvestmentEvent!)).ToDictionary(g => g.Key, BuildDashboardEvents);
        return analyses.Select(a => byAnalysis.TryGetValue(a.Id, out var assessment)
            ? CreateDashboardAssessment(assessment, events.GetValueOrDefault(RadarEventPolicy.CardId(assessment.InvestmentEvent!)))
            : CreateDashboardDocument(a)).ToList();
    }

    private static IReadOnlyList<RadarEventDto> BuildDashboardEvents(IEnumerable<PortfolioAssessment> assessments)
    {
        var representative = RadarAssessmentSelector.SelectRepresentative(assessments);
        return assessments
            .Where(a => a.InvestmentEvent is not null)
            .Select(a => a.InvestmentEvent!)
            .GroupBy(e => e.Id)
            .Select(group => group.First())
            .OrderByDescending(e => e.Id == representative.InvestmentEventId)
            .ThenByDescending(e => e.OccurredAt.HasValue)
            .ThenByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Select(e => new RadarEventDto(e.Id, e.EventType, e.OccurredAt, e.Summary))
            .ToList();
    }

    private static DashboardAssessment CreateDashboardAssessment(PortfolioAssessment assessment, IReadOnlyList<RadarEventDto>? events = null)
        // The overview is document-oriented. An extracted event can have a different
        // occurrence date, so use the document publication date in its card.
        => new(assessment.Id, assessment.InvestmentEventId, assessment.InvestmentEvent?.Evidence.FirstOrDefault()?.DocumentId, assessment.InvestmentEvent?.AffectedAssets.FirstOrDefault()?.Ticker ?? "Carteira", assessment.InvestmentEvent?.Summary ?? assessment.Summary, assessment.WhyItMatters, assessment.SuggestedBehavior, assessment.InvestmentEvent?.DocumentAnalysis?.Document?.PublishedAt ?? assessment.InvestmentEvent?.OccurredAt, assessment.AttentionPriority, assessment.ActionRequirement, assessment.CapitalDecisionImpact, assessment.Status, assessment.InvestmentEvent is { } investmentEvent ? RadarEventPolicy.Importance(investmentEvent).ToString() : "Unknown", true, events ?? Array.Empty<RadarEventDto>());

    private static DashboardAssessment CreateDashboardDocument(DocumentAnalysis analysis)
        => new(analysis.Id, analysis.Id, analysis.DocumentId, analysis.Document?.Asset?.Ticker ?? "Carteira", analysis.Summary, analysis.Impact, analysis.SuggestedAction.ToString(), analysis.Document?.PublishedAt, AttentionPriority.Low, ActionRequirement.None, CapitalDecisionImpact.None, AssessmentStatus.Active, analysis.Importance.ToString(), false);

    private static string HealthLabel(PortfolioHealthStatus status) => status switch
    {
        PortfolioHealthStatus.Aligned => "Carteira alinhada à estratégia.", PortfolioHealthStatus.SlightlyUnbalanced => "Carteira levemente fora da estratégia.", PortfolioHealthStatus.Unbalanced => "Carteira fora da estratégia.", PortfolioHealthStatus.StronglyUnbalanced => "Carteira significativamente fora da estratégia.", PortfolioHealthStatus.NotConfigured => "Estratégia ainda não configurada.", PortfolioHealthStatus.EmptyPortfolio => "Carteira ainda sem posições.", _ => "Estado da carteira indisponível."
    };
    private static string DeviationLabel(MainDeviation deviation) => $"{AssetClassLabel(deviation.AssetClass)} estão {Math.Abs(deviation.DeviationPercentagePoints):0.##} p.p. {(deviation.Direction == AllocationDeviationDirection.Underweight ? "abaixo" : "acima")} da alocação definida.";
    private static string AssetClassLabel(AssetClass assetClass) => assetClass switch { AssetClass.Fii => "FIIs", AssetClass.BrazilianStock => "Ações", AssetClass.FixedIncome => "Renda fixa", AssetClass.International => "ETF", _ => "Outros ativos" };

    [HttpGet("{portfolioId:guid}/health")]
    public async Task<ActionResult<PortfolioHealthResult>> GetHealth(Guid portfolioId, CancellationToken cancellationToken)
    {
        var portfolio = await dbContext.Portfolios.Include(p => p.Positions).ThenInclude(p => p.Asset).SingleOrDefaultAsync(p => p.Id == portfolioId, cancellationToken);
        if (portfolio is null) return NotFound();
        var targets = await dbContext.TargetAllocations.Where(x => x.PortfolioId == portfolioId).ToListAsync(cancellationToken);
        return Ok(portfolioHealthCalculator.Calculate(portfolio, targets));
    }

    [HttpPost("{portfolioId:guid}/positions")]
    public async Task<ActionResult<PositionResponse>> AddPosition(
        Guid portfolioId,
        PositionRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (dbContext.Database.IsRelational())
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Portfolios\" WHERE \"Id\" = {portfolioId} FOR UPDATE", cancellationToken);
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
        portfolio.Positions.Add(position);
        dbContext.PortfolioPositions.Add(position);
        if (request.RegisterPurchase)
        {
            if (request.Quantity <= 0 || request.AveragePrice <= 0)
            {
                return BadRequest("Para registrar a compra, informe quantidade e preço médio positivos.");
            }

            dbContext.PortfolioTransactions.Add(CreateManualBuy(
                portfolio.Id,
                asset.Id,
                request.Quantity,
                request.AveragePrice,
                request.OccurredAt));
        }
        Recalculate(portfolio);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (documentBootstrap is not null)
        {
            try
            {
                await documentBootstrap.DiscoverLatestOnlyAsync(new[] { asset.Id }, cancellationToken);
            }
            catch (Exception exception)
            {
                logger?.LogError(exception, "Falha no discovery após adição manual: PortfolioId={PortfolioId}, AssetId={AssetId}", portfolioId, asset.Id);
            }
        }

        return CreatedAtAction(nameof(GetPortfolio), new { portfolioId }, CreatePositionResponse(position));
    }

    [HttpPut("{portfolioId:guid}/positions/{positionId:guid}")]
    public async Task<ActionResult<PositionResponse>> UpdatePosition(
        Guid portfolioId,
        Guid positionId,
        PositionUpdateRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (dbContext.Database.IsRelational())
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Portfolios\" WHERE \"Id\" = {portfolioId} FOR UPDATE", cancellationToken);
        var portfolio = await LoadPortfolio(portfolioId, cancellationToken);
        var position = portfolio?.Positions.SingleOrDefault(item => item.Id == positionId);
        if (position is null) return NotFound();

        try
        {
            var addedQuantity = request.Quantity - position.Quantity;
            var purchaseUnitPrice = request.PurchaseUnitPrice ?? request.AveragePrice;
            if (request.RegisterPurchase && (addedQuantity <= 0 || purchaseUnitPrice <= 0))
            {
                return BadRequest("Para registrar a compra, aumente a quantidade e informe um preço unitário positivo.");
            }

            if (request.RegisterPurchase)
            {
                // The execution price belongs to the added units, not the whole position.
                position.ApplyBuy(addedQuantity, purchaseUnitPrice);
                position.Update(position.Quantity, position.AveragePrice, request.CurrentPrice);
                dbContext.PortfolioTransactions.Add(CreateManualBuy(
                    portfolio!.Id,
                    position.AssetId,
                    addedQuantity,
                    purchaseUnitPrice,
                    request.OccurredAt));
            }
            else position.Update(request.Quantity, request.AveragePrice, request.CurrentPrice);
            Recalculate(portfolio!);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
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
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (dbContext.Database.IsRelational())
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Portfolios\" WHERE \"Id\" = {portfolioId} FOR UPDATE", cancellationToken);
        var portfolio = await LoadPortfolio(portfolioId, cancellationToken);
        var position = portfolio?.Positions.SingleOrDefault(item => item.Id == positionId);
        if (position is null) return NotFound();

        dbContext.PortfolioPositions.Remove(position);
        portfolio!.Positions.Remove(position);
        Recalculate(portfolio);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private async Task<Portfolio?> LoadPortfolio(Guid portfolioId, CancellationToken cancellationToken)
    {
        return await dbContext.Portfolios
            .Include(portfolio => portfolio.Positions)
            .ThenInclude(position => position.Asset)
            .ThenInclude(asset => asset!.GeographicExposures)
            .Include(portfolio => portfolio.Positions)
            .ThenInclude(position => position.Asset)
            .ThenInclude(asset => asset!.CurrencyExposures)
            .SingleOrDefaultAsync(portfolio => portfolio.Id == portfolioId, cancellationToken);
    }

    private static PortfolioTransaction CreateManualBuy(Guid portfolioId, Guid assetId, decimal quantity, decimal unitPrice, DateTimeOffset? occurredAt)
    {
        var total = decimal.Round(quantity * unitPrice, 2);
        var transaction = new PortfolioTransaction(portfolioId, PortfolioTransactionType.Buy, occurredAt ?? DateTimeOffset.UtcNow, null, total, source: FinancialSource.Manual);
        transaction.Items.Add(new PortfolioTransactionItem(transaction.Id, assetId, quantity, unitPrice));
        return transaction;
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
            position.PortfolioWeight,
            position.Asset is null ? null : new ExposureMetadataResponse(
                position.Asset.SectorCategory?.ToString(),
                position.Asset.ListingCountryCode,
                position.Asset.TradingCurrency,
                position.Asset.GeographicExposureCoverage.ToString(),
                position.Asset.GeographicExposures.Select(x => new GeographicExposureResponse(x.CountryCode, x.Percentage, x.Source.ToString(), x.SourceReference, x.ObservedAt, x.IsDerived)).ToArray(),
                position.Asset.EconomicCurrencyExposureCoverage.ToString(),
                position.Asset.CurrencyExposures.Select(x => new CurrencyExposureResponse(x.CurrencyCode, x.Percentage, x.Source.ToString(), x.SourceReference, x.ObservedAt, x.IsDerived)).ToArray()));
    }
}

public sealed record CreatePortfolioRequest(string Name);
public sealed record RenamePortfolioRequest(string Name);
public sealed record PositionRequest(string Ticker, string Name, AssetType AssetType, string? Sector, decimal Quantity, decimal AveragePrice, decimal? CurrentPrice, AssetClass? AssetClass = null, bool RegisterPurchase = false, DateTimeOffset? OccurredAt = null);
public sealed record PositionUpdateRequest(decimal Quantity, decimal AveragePrice, decimal? CurrentPrice, bool RegisterPurchase = false, DateTimeOffset? OccurredAt = null, decimal? PurchaseUnitPrice = null);
public sealed record PortfolioSummaryResponse(Guid Id, string Name, decimal TotalValue, int PositionCount);
public sealed record PortfolioDetailResponse(Guid Id, string Name, decimal TotalValue, IReadOnlyList<PositionResponse> Positions);
public enum DashboardState { Ready, FirstUse, Processing, StrategyNotConfigured }
public enum ContributionPreviewStatus { Available, NotConfigured, NoNeeds, EmptyPortfolio }
public sealed record PortfolioOverview(Guid PortfolioId, string PortfolioName, decimal CurrentValue, string HealthStatus, string HealthSummary, string? MainDeviationSummary, DateTimeOffset? LastPortfolioUpdate);
public sealed record ContributionPreview(ContributionPreviewStatus Status, AssetClass? PrimaryCapitalNeed, string Summary);
public sealed record DashboardAssessment(Guid AssessmentId, Guid InvestmentEventId, Guid? DocumentId, string Asset, string EventSummary, string WhyItMatters, string SuggestedBehavior, DateTimeOffset? OccurredAt, AttentionPriority AttentionPriority, ActionRequirement ActionRequirement, CapitalDecisionImpact CapitalDecisionImpact, AssessmentStatus Status, string DocumentImportance = "Unknown", bool HasRadarAssessment = true, IReadOnlyList<RadarEventDto>? Events = null);
public sealed record PortfolioHealthSummary(string Status, string Summary, IReadOnlyList<AllocationDeviation> AllocationDeviations, MainDeviation? MainDeviation);
public sealed record DashboardResponse(Guid Id, string Name, decimal TotalValue, int PositionCount, IReadOnlyList<PositionResponse> Positions, int Page = 1, int PageSize = 10, int TotalItems = 0, int TotalPages = 0, DashboardState State = DashboardState.Ready, PortfolioOverview? PortfolioOverview = null, IReadOnlyList<DashboardAssessment>? AttentionNow = null, ContributionPreview? ContributionPreview = null, PortfolioHealthSummary? PortfolioHealthSummary = null, IReadOnlyList<DashboardAssessment>? RecentUpdates = null, DateTimeOffset? GeneratedAt = null, int RecentUpdatesTotalItems = 0);
public sealed record PositionResponse(Guid Id, Guid AssetId, AssetClass AssetClass, string Ticker, string Name, AssetType AssetType, string? Sector, decimal Quantity, decimal AveragePrice, decimal? CurrentPrice, decimal? CurrentValue, decimal PortfolioWeight, ExposureMetadataResponse? Exposure = null);
public sealed record ExposureMetadataResponse(string? SectorCategory, string? ListingCountryCode, string? TradingCurrency, string GeographicCoverage, IReadOnlyList<GeographicExposureResponse> GeographicExposures, string EconomicCurrencyCoverage, IReadOnlyList<CurrencyExposureResponse> CurrencyExposures);
public sealed record GeographicExposureResponse(string CountryCode, decimal? Percentage, string Source, string? SourceReference, DateTimeOffset? ObservedAt, bool IsDerived);
public sealed record CurrencyExposureResponse(string CurrencyCode, decimal? Percentage, string Source, string? SourceReference, DateTimeOffset? ObservedAt, bool IsDerived);
