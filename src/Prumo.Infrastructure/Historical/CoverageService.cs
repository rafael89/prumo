using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Infrastructure.Historical;

public sealed class CoverageService(PrumoDbContext db)
{
    public async Task<IReadOnlyList<CoverageEvidenceDto>> List(Guid portfolioId, CancellationToken ct, FinancialHistoryCoverageDimension? dimension = null, Guid? assetId = null)
        => await db.FinancialHistoryCoverages.AsNoTracking().Where(x => x.PortfolioId == portfolioId && (!dimension.HasValue || x.Dimension == dimension) && (!assetId.HasValue || x.AssetId == assetId || x.AssetId == null)).OrderBy(x => x.FromDate).Select(x => new CoverageEvidenceDto(x.Id, x.Dimension.ToString(), x.AssetId, x.Asset == null ? null : x.Asset.Ticker, x.FromDate, x.ToDate, x.Source.ToString(), x.Notes, x.CreatedAt)).ToListAsync(ct);

    public async Task<CoverageEvidenceDto> Create(Guid portfolioId, CoverageRequest request, CancellationToken ct)
    {
        if (request.FromDate > request.ToDate) throw new ArgumentException("O início deve ser anterior ou igual ao fim.");
        if (!await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct)) throw new KeyNotFoundException("Carteira não encontrada.");
        if (request.AssetId is { } assetId && !await db.Assets.AnyAsync(x => x.Id == assetId && x.IsActive, ct)) throw new ArgumentException("O ativo informado é inválido.");
        var evidence = new Prumo.Domain.Entities.FinancialHistoryCoverage(portfolioId, request.Dimension, request.FromDate, request.ToDate, request.Source, request.AssetId, request.Notes);
        db.FinancialHistoryCoverages.Add(evidence); await db.SaveChangesAsync(ct);
        return (await List(portfolioId, ct)).Single(x => x.Id == evidence.Id);
    }

    public async Task<bool> Delete(Guid portfolioId, Guid coverageId, CancellationToken ct)
    {
        var evidence = await db.FinancialHistoryCoverages.SingleOrDefaultAsync(x => x.Id == coverageId && x.PortfolioId == portfolioId, ct);
        if (evidence is null) return false; db.FinancialHistoryCoverages.Remove(evidence); await db.SaveChangesAsync(ct); return true;
    }

    public async Task<CoverageStatusDto> Resolve(Guid portfolioId, FinancialHistoryCoverageDimension dimension, DateOnly from, DateOnly to, Guid? assetId, CancellationToken ct)
    {
        if (from > to) throw new ArgumentException("O início deve ser anterior ou igual ao fim.");
        var evidence = await db.FinancialHistoryCoverages.AsNoTracking().Where(x => x.PortfolioId == portfolioId && x.Dimension == dimension && (!assetId.HasValue || x.AssetId == assetId || x.AssetId == null) && x.FromDate <= to && x.ToDate >= from).OrderBy(x => x.FromDate).Select(x => new CoverageEvidenceDto(x.Id, x.Dimension.ToString(), x.AssetId, x.Asset == null ? null : x.Asset.Ticker, x.FromDate, x.ToDate, x.Source.ToString(), x.Notes, x.CreatedAt)).ToListAsync(ct);
        var cursor = from;
        foreach (var item in evidence)
        {
            if (item.FromDate > cursor) return new(dimension.ToString(), assetId, from, to, "Partial", ["PartialIntervalCoverage"], evidence);
            if (item.ToDate >= cursor) cursor = item.ToDate.AddDays(1);
            if (cursor > to) return new(dimension.ToString(), assetId, from, to, "Complete", [], evidence);
        }
        return new(dimension.ToString(), assetId, from, to, evidence.Count == 0 ? "Unavailable" : "Partial", [evidence.Count == 0 ? "MissingCoverageEvidence" : "PartialIntervalCoverage"], evidence);
    }
}

public sealed record CoverageRequest(FinancialHistoryCoverageDimension Dimension, DateOnly FromDate, DateOnly ToDate, Prumo.Domain.Enums.FinancialSource Source, Guid? AssetId = null, string? Notes = null);
public sealed record CoverageEvidenceDto(Guid Id, string Dimension, Guid? AssetId, string? Ticker, DateOnly FromDate, DateOnly ToDate, string Source, string? Notes, DateTimeOffset CreatedAt);
public sealed record CoverageStatusDto(string Dimension, Guid? AssetId, DateOnly FromDate, DateOnly ToDate, string Status, IReadOnlyList<string> Reasons, IReadOnlyList<CoverageEvidenceDto> Evidence);
