using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Api.Services;

/// <summary>Current evidence per metric, retaining peers needed to detect conflicts.</summary>
public static class ContributionFinancialQueries
{
    private static Expression<Func<AssetFundamentalSnapshot, bool>> Fundamentals(PrumoDbContext db)
        => row =>
            row.Revenue != null && !db.AssetFundamentalSnapshots.Any(newer => newer.AssetId == row.AssetId && newer.ReferenceDate > row.ReferenceDate && newer.Revenue != null) ||
            row.NetIncome != null && !db.AssetFundamentalSnapshots.Any(newer => newer.AssetId == row.AssetId && newer.ReferenceDate > row.ReferenceDate && newer.NetIncome != null) ||
            row.Equity != null && !db.AssetFundamentalSnapshots.Any(newer => newer.AssetId == row.AssetId && newer.ReferenceDate > row.ReferenceDate && newer.Equity != null) ||
            row.TotalDebt != null && !db.AssetFundamentalSnapshots.Any(newer => newer.AssetId == row.AssetId && newer.ReferenceDate > row.ReferenceDate && newer.TotalDebt != null) ||
            row.Cash != null && !db.AssetFundamentalSnapshots.Any(newer => newer.AssetId == row.AssetId && newer.ReferenceDate > row.ReferenceDate && newer.Cash != null) ||
            row.Ebitda != null && !db.AssetFundamentalSnapshots.Any(newer => newer.AssetId == row.AssetId && newer.ReferenceDate > row.ReferenceDate && newer.Ebitda != null) ||
            row.Roe != null && !db.AssetFundamentalSnapshots.Any(newer => newer.AssetId == row.AssetId && newer.ReferenceDate > row.ReferenceDate && newer.Roe != null) ||
            row.Roic != null && !db.AssetFundamentalSnapshots.Any(newer => newer.AssetId == row.AssetId && newer.ReferenceDate > row.ReferenceDate && newer.Roic != null) ||
            row.NetMargin != null && !db.AssetFundamentalSnapshots.Any(newer => newer.AssetId == row.AssetId && newer.ReferenceDate > row.ReferenceDate && newer.NetMargin != null);

    private static Expression<Func<AssetValuationSnapshot, bool>> Valuations(PrumoDbContext db)
        => row =>
            row.PriceEarnings != null && !db.AssetValuationSnapshots.Any(newer => newer.AssetId == row.AssetId && newer.ReferenceDate > row.ReferenceDate && newer.PriceEarnings != null) ||
            row.PriceBook != null && !db.AssetValuationSnapshots.Any(newer => newer.AssetId == row.AssetId && newer.ReferenceDate > row.ReferenceDate && newer.PriceBook != null) ||
            row.EvEbitda != null && !db.AssetValuationSnapshots.Any(newer => newer.AssetId == row.AssetId && newer.ReferenceDate > row.ReferenceDate && newer.EvEbitda != null) ||
            row.DividendYield != null && !db.AssetValuationSnapshots.Any(newer => newer.AssetId == row.AssetId && newer.ReferenceDate > row.ReferenceDate && newer.DividendYield != null);

    private static Expression<Func<DocumentFinancialFact, bool>> Facts(PrumoDbContext db)
        => row => !db.DocumentFinancialFacts.Any(newer => newer.AssetId == row.AssetId && newer.Metric == row.Metric && newer.ReferencePeriod > row.ReferencePeriod);

    public static IQueryable<Asset> WithContributionFinancials(this IQueryable<Asset> assets, PrumoDbContext db)
        => assets.AsSplitQuery()
            .Include(x => x.FundamentalSnapshots.AsQueryable().Where(Fundamentals(db)))
            .Include(x => x.ValuationSnapshots.AsQueryable().Where(Valuations(db)))
            .Include(x => x.DocumentFinancialFacts.AsQueryable().Where(Facts(db)))
            // Keep official restatements together, including nulls that supersede older values.
            .Include(x => x.CvmFiiMonthlyObservations);

    public static IQueryable<Portfolio> WithContributionFinancials(this IQueryable<Portfolio> portfolios, PrumoDbContext db)
        => portfolios.AsNoTracking().AsSplitQuery()
            .Include(x => x.Positions).ThenInclude(x => x.Asset).ThenInclude(x => x!.GeographicExposures)
            .Include(x => x.Positions).ThenInclude(x => x.Asset).ThenInclude(x => x!.CurrencyExposures)
            .Include(x => x.Positions).ThenInclude(x => x.Asset).ThenInclude(x => x!.FundamentalSnapshots.AsQueryable().Where(Fundamentals(db)))
            .Include(x => x.Positions).ThenInclude(x => x.Asset).ThenInclude(x => x!.ValuationSnapshots.AsQueryable().Where(Valuations(db)))
            .Include(x => x.Positions).ThenInclude(x => x.Asset).ThenInclude(x => x!.DocumentFinancialFacts.AsQueryable().Where(Facts(db)))
            .Include(x => x.Positions).ThenInclude(x => x.Asset).ThenInclude(x => x!.CvmFiiMonthlyObservations);
}
