using Microsoft.EntityFrameworkCore;
using Prumo.Application.Radar;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Api.Services;

internal static class RadarCardReader
{
    public static async Task<IReadOnlyDictionary<Guid, Guid>> ReadAsync(PrumoDbContext db, Guid portfolioId, CancellationToken ct)
    {
        var sources = await db.PortfolioAssessments.AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId && x.InvestmentEvent!.DocumentAnalysis!.Document != null)
            .Select(x => new RadarCardSource(x.InvestmentEvent!.DocumentAnalysisId,
                x.InvestmentEvent.DocumentAnalysis!.Document!.AssetId,
                x.InvestmentEvent.DocumentAnalysis.Document.Asset!.AssetType,
                x.InvestmentEvent.DocumentAnalysis.Document.PublishedAt,
                x.InvestmentEvent.EventType, x.InvestmentEvent.Summary)).ToListAsync(ct);
        return RadarCardGrouping.Build(sources);
    }
}
