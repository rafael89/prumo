using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Domain.Rules;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/portfolios")]
public sealed class PortfoliosController : ControllerBase
{
    private readonly PrumoDbContext dbContext;

    public PortfoliosController(PrumoDbContext dbContext)
    {
        this.dbContext = dbContext;
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
    public async Task<ActionResult<DashboardResponse>> GetDashboard(Guid portfolioId, CancellationToken cancellationToken)
    {
        var portfolio = await LoadPortfolio(portfolioId, cancellationToken);
        if (portfolio is null) return NotFound();

        return Ok(new DashboardResponse(
            portfolio.Id,
            portfolio.Name,
            PortfolioCalculator.CalculateTotalValue(portfolio.Positions),
            portfolio.Positions.Count,
            portfolio.Positions
                .OrderByDescending(position => position.PortfolioWeight)
                .Select(CreatePositionResponse)
                .ToList()));
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
            asset = new Asset(ticker, request.Name, request.AssetType, request.Sector);
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
public sealed record PositionRequest(string Ticker, string Name, AssetType AssetType, string? Sector, decimal Quantity, decimal AveragePrice, decimal? CurrentPrice);
public sealed record PositionUpdateRequest(decimal Quantity, decimal AveragePrice, decimal? CurrentPrice);
public sealed record PortfolioSummaryResponse(Guid Id, string Name, decimal TotalValue, int PositionCount);
public sealed record PortfolioDetailResponse(Guid Id, string Name, decimal TotalValue, IReadOnlyList<PositionResponse> Positions);
public sealed record DashboardResponse(Guid Id, string Name, decimal TotalValue, int PositionCount, IReadOnlyList<PositionResponse> Positions);
public sealed record PositionResponse(Guid Id, string Ticker, string Name, AssetType AssetType, string? Sector, decimal Quantity, decimal AveragePrice, decimal? CurrentPrice, decimal? CurrentValue, decimal PortfolioWeight);
