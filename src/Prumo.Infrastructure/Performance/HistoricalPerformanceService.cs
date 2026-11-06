using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Historical;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Infrastructure.Performance;

public sealed class HistoricalPerformanceService(PrumoDbContext db, HistoricalFinancialStateService state, CoverageService coverage)
{
    public async Task<HistoricalPerformanceDto?> Get(Guid portfolioId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (from > to) return null;
        var data = await state.LoadAsync(portfolioId, to, ct);
        if (!data.Exists) return null;
        var startState = await state.Get(portfolioId, from, null, ct, null, data); var endState = await state.Get(portfolioId, to, null, ct, null, data);
        if (startState is null || endState is null) return null;
        var ids = startState.Assets.Concat(endState.Assets).Select(x => x.AssetId)
            .Concat(data.Transactions.Where(x => DateOnly.FromDateTime(x.OccurredAt.Date) >= from && DateOnly.FromDateTime(x.OccurredAt.Date) <= to).SelectMany(x => x.Items).Select(x => x.AssetId))
            .Concat(data.Anchors.Where(x => x.AnchorDate >= from && x.AnchorDate <= to).SelectMany(x => x.Items).Select(x => x.AssetId)).Distinct().ToList();
        var prices = await db.AssetPrices.AsNoTracking().Where(x => ids.Contains(x.AssetId) && x.PriceDate <= to).OrderByDescending(x => x.PriceDate).ThenByDescending(x => x.FetchedAt).ToListAsync(ct);
        var pricesByAsset = prices.GroupBy(x => x.AssetId).ToDictionary(x => x.Key, x => x.OrderBy(p => p.PriceDate).ThenBy(p => p.FetchedAt).ToArray());
        var start = BuildValuation(startState, pricesByAsset, from); var end = BuildValuation(endState, pricesByAsset, to);
        var flows = await BuildFlows(portfolioId, from, to, ct);
        var change = Metric(start.Value.HasValue && end.Value.HasValue ? end.Value - start.Value : null, Min(start.Coverage, end.Coverage), start.Reasons.Concat(end.Reasons));
        var totalChange = Metric(start.TotalValue.HasValue && end.TotalValue.HasValue ? end.TotalValue - start.TotalValue : null, Min(start.TotalCoverage, end.TotalCoverage), start.Reasons.Concat(end.Reasons).Concat(start.TotalReasons ?? []).Concat(end.TotalReasons ?? []));
        var seriesDates = prices.Select(x => x.PriceDate).Where(x => x >= from).Distinct().OrderBy(x => x).ToList();
        if (!seriesDates.Contains(from)) seriesDates.Insert(0, from); if (!seriesDates.Contains(to)) seriesDates.Add(to);
        var series = new List<HistoricalValuationPointDto>();
        foreach (var date in seriesDates) { var pointState = date == from ? startState : date == to ? endState : await state.Get(portfolioId, date, null, ct, null, data); if (pointState is not null) { var point = BuildValuation(pointState, pricesByAsset, date); series.Add(new(date, point.Value, point.Coverage, point.Reasons)); } }
        return new(portfolioId, from, to, start, end, change, flows.Contributions, flows.Withdrawals, flows.Net, flows.Income, null, series, startState.Warnings.Concat(endState.Warnings).Distinct().ToList(), totalChange);
    }

    private async Task<FlowSummaryDto> BuildFlows(Guid portfolioId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var flows = await db.PortfolioCashFlows.AsNoTracking().Where(x => x.PortfolioId == portfolioId && DateOnly.FromDateTime(x.OccurredAt.UtcDateTime) >= from && DateOnly.FromDateTime(x.OccurredAt.UtcDateTime) <= to).ToListAsync(ct);
        var evidence = await coverage.Resolve(portfolioId, FinancialHistoryCoverageDimension.CashFlows, from, to, null, ct);
        PerformanceMetricDto Sum(PortfolioCashFlowType type) => new(flows.Where(x => x.Type == type).Sum(x => x.Amount), evidence.Status, evidence.Status == "Unavailable" ? ["IncompleteCashFlowHistory"] : evidence.Reasons);
        var contributions = Sum(PortfolioCashFlowType.Contribution); var withdrawals = Sum(PortfolioCashFlowType.Withdrawal); var income = Sum(PortfolioCashFlowType.Income);
        return new(contributions, withdrawals, new(contributions.Value - withdrawals.Value, Min(contributions.Coverage, withdrawals.Coverage), contributions.Reasons.Concat(withdrawals.Reasons).Distinct().ToList()), income);
    }

    private static HistoricalValuationDto BuildValuation(HistoricalPortfolioStateDto historical, IReadOnlyDictionary<Guid, Prumo.Domain.Entities.AssetPrice[]> prices, DateOnly date)
    {
        if (historical.Anchor is null)
            return new(null, "Unavailable", historical.Warnings, [], historical.Cash?.Balance,
                historical.Cash?.Coverage ?? "Unavailable", null, "Unavailable", historical.Cash?.Warnings ?? []);
        var assets = historical.Assets.Select(asset =>
        {
            var price = FindPrice(prices.GetValueOrDefault(asset.AssetId) ?? [], date);
            var priceCoverage = asset.Quantity is null ? "Unavailable" : asset.Quantity == 0 ? "Complete" : price is null ? "Unavailable" : "Complete";
            var value = asset.Quantity is null || price is null && asset.Quantity != 0 ? null : asset.Quantity * (asset.Quantity == 0 ? 0 : price!.Close);
            var valuationCoverage = value is null ? "Unavailable" : Min(asset.QuantityCoverage, priceCoverage);
            return new HistoricalAssetValuationDto(asset.AssetId, asset.Ticker, asset.Quantity, price?.Close, price?.PriceDate, value, asset.QuantityCoverage, priceCoverage, valuationCoverage, asset.Warnings.Concat(value is null && asset.Quantity != 0 ? ["MissingMarketPrice"] : []).Distinct().ToList());
        }).ToList();
        var known = assets.Where(x => x.MarketValue.HasValue).ToList(); var coverage = assets.Count == 0 ? "Complete" : known.Count == 0 ? "Unavailable" : known.Count == assets.Count ? Min(assets.Select(x => x.ValuationCoverage).ToArray()) : "Partial";
        decimal? invested = assets.Count == 0 ? 0 : known.Count == 0 ? (decimal?)null : known.Sum(x => x.MarketValue);
        var cash = historical.Cash;
        decimal? total = invested.HasValue && cash?.Balance.HasValue == true ? invested.Value + cash.Balance.Value : null;
        var totalCoverage = total.HasValue ? Min(coverage, cash!.Coverage) : "Unavailable";
        return new(invested, coverage, assets.SelectMany(x => x.Warnings).Concat(cash?.Warnings ?? []).Distinct().ToList(), assets, cash?.Balance, cash?.Coverage ?? "Unavailable", total, totalCoverage, cash?.Warnings ?? []);
    }

    private static Prumo.Domain.Entities.AssetPrice? FindPrice(Prumo.Domain.Entities.AssetPrice[] prices, DateOnly date)
    {
        var low = 0; var high = prices.Length;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (prices[mid].PriceDate <= date) low = mid + 1; else high = mid;
        }
        return low == 0 ? null : prices[low - 1];
    }

    private static PerformanceMetricDto Metric(decimal? value, string coverage, IEnumerable<string> reasons) => new(value, coverage, reasons.Distinct().ToList());
    private static string Min(params string[] values) => values.Contains("Unavailable") ? "Unavailable" : values.Contains("Partial") ? "Partial" : "Complete";
}

public sealed record HistoricalPerformanceDto(Guid PortfolioId, DateOnly From, DateOnly To, HistoricalValuationDto StartingValuation, HistoricalValuationDto EndingValuation, PerformanceMetricDto ValueChange, PerformanceMetricDto Contributions, PerformanceMetricDto Withdrawals, PerformanceMetricDto NetExternalFlows, PerformanceMetricDto RecordedIncome, PerformanceMetricDto? ReturnMetric, IReadOnlyList<HistoricalValuationPointDto> Series, IReadOnlyList<string> Warnings, PerformanceMetricDto? TotalValueChange = null);
public sealed record HistoricalValuationDto(decimal? Value, string Coverage, IReadOnlyList<string> Reasons, IReadOnlyList<HistoricalAssetValuationDto> Assets, decimal? CashBalance = null, string CashCoverage = "Unavailable", decimal? TotalValue = null, string TotalCoverage = "Unavailable", IReadOnlyList<string>? TotalReasons = null);
public sealed record HistoricalAssetValuationDto(Guid AssetId, string Ticker, decimal? Quantity, decimal? Price, DateOnly? PriceDate, decimal? MarketValue, string QuantityCoverage, string PriceCoverage, string ValuationCoverage, IReadOnlyList<string> Warnings);
public sealed record HistoricalValuationPointDto(DateOnly Date, decimal? InvestedValue, string Coverage, IReadOnlyList<string> Reasons);
public sealed record PerformanceMetricDto(decimal? Value, string Coverage, IReadOnlyList<string> Reasons);
public sealed record FlowSummaryDto(PerformanceMetricDto Contributions, PerformanceMetricDto Withdrawals, PerformanceMetricDto Net, PerformanceMetricDto Income);
