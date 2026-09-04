using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Reconciliation;

namespace Prumo.Infrastructure.Historical;

public sealed class HistoricalCompletenessService(PrumoDbContext db, CoverageService coverage, HistoricalFinancialStateService state, HistoricalCashStateService cash)
{
    public async Task<PortfolioHistoricalCompletenessDto?> Get(Guid portfolioId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (!await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct)) return null;
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow); var assetAnchors = await db.PortfolioHistoricalAnchors.AsNoTracking().Include(x => x.Items).Where(x => x.PortfolioId == portfolioId && x.AnchorDate <= end).OrderBy(x => x.AnchorDate).ToListAsync(ct);
        var start = from ?? assetAnchors.FirstOrDefault()?.AnchorDate; var historical = await state.Get(portfolioId, end, null, ct); var cashState = await cash.Get(portfolioId, end, ct);
        if (historical is null) return null;
        var continuity = await Continuity(portfolioId, assetAnchors, ct);
        var baseDate = start ?? end; var dimensions = new List<HistoricalCompletenessDimensionDto>();
        var position = Combine([await AssetCoverage(portfolioId, FinancialHistoryCoverageDimension.Position, baseDate, end, historical.Assets.Select(x => x.AssetId), ct)], continuity.Where(x => x.Dimension == "Position" && x.Status == "Mismatch").Any(), "AnchorQuantityMismatch");
        var transactions = await PortfolioCoverage(portfolioId, FinancialHistoryCoverageDimension.Transactions, baseDate, end, ct);
        var costBasis = Combine([await AssetCoverage(portfolioId, FinancialHistoryCoverageDimension.CostBasis, baseDate, end, historical.Assets.Select(x => x.AssetId), ct)], continuity.Any(x => x.Dimension == "CostBasis" && x.Status == "Mismatch"), "AnchorCostBasisMismatch");
        var corporate = await PortfolioCoverage(portfolioId, FinancialHistoryCoverageDimension.CorporateActions, baseDate, end, ct);
        var cashCoverage = cashState?.Coverage ?? "Unavailable";
        var cashDimension = new DimensionResult(cashCoverage, cashState?.Warnings ?? [], cashState?.ReliableFrom);
        var cashFlows = await PortfolioCoverage(portfolioId, FinancialHistoryCoverageDimension.CashFlows, baseDate, end, ct);
        var income = await PortfolioCoverage(portfolioId, FinancialHistoryCoverageDimension.Income, baseDate, end, ct);
        var prices = await PriceCoverage(portfolioId, end, baseDate, ct);
        var datePrecision = historical.Assets.Any(x => x.Warnings.Any(IsDateWarning)) || cashState?.Warnings.Any(IsDateWarning) == true ? new DimensionResult("Partial", ["ApproximateDate"], null) : new("Complete", [], baseDate);
        if (historical.Warnings.Contains("UnresolvedTransactionReconciliation") || historical.Assets.Any(x => x.Warnings.Contains("UnresolvedTransactionReconciliation"))) transactions = Degrade(transactions, "UnresolvedTransactionReconciliation");
        if (historical.Assets.Any(x => x.Warnings.Contains("ApproximateCorporateActionDate"))) corporate = Degrade(corporate, "ApproximateCorporateActionDate");
        if (historical.Assets.Any(x => x.Warnings.Contains("Oversell") || x.Warnings.Contains("UnknownOpeningState"))) position = Degrade(position, "HistoricalPositionWarning");
        dimensions.AddRange([Dimension("Position", position), Dimension("Transactions", transactions), Dimension("CostBasis", costBasis), Dimension("Cash", cashDimension), Dimension("CashFlows", cashFlows), Dimension("Income", income), Dimension("CorporateActions", corporate), Dimension("MarketPrices", prices), Dimension("DatePrecision", datePrecision)]);
        var blockers = dimensions.Where(x => x.Status != "Complete").SelectMany(x => x.Reasons.Select(reason => $"{x.Name}:{reason}")).Distinct().ToList();
        var overall = historical.Anchor is null && cashState?.AnchorId is null ? "Unavailable" : dimensions.All(x => x.Status == "Complete") ? "Complete" : dimensions.Any(x => x.Status != "Unavailable") ? "Partial" : "Unavailable";
        var reliableFrom = overall == "Complete" ? dimensions.Max(x => x.ReliableFrom) : null;
        var warnings = historical.Warnings.Concat(cashState?.Warnings ?? []).Distinct().ToList();
        return new(portfolioId, overall, reliableFrom, dimensions, continuity, blockers, warnings, Metrics(dimensions, historical, cashState, transactions, cashFlows, income));
    }

    private async Task<IReadOnlyList<HistoricalContinuityCheckDto>> Continuity(Guid portfolioId, IReadOnlyList<Prumo.Domain.Entities.PortfolioHistoricalAnchor> anchors, CancellationToken ct)
    {
        var result = new List<HistoricalContinuityCheckDto>();
        for (var i = 1; i < anchors.Count; i++)
        {
            var from = anchors[i - 1]; var to = anchors[i]; var expected = await state.Get(portfolioId, to.AnchorDate, null, ct, from.Id);
            foreach (var assetId in from.Items.Select(x => x.AssetId).Concat(to.Items.Select(x => x.AssetId)).Distinct())
            {
                var expectedItem = expected?.Assets.SingleOrDefault(x => x.AssetId == assetId); var actualItem = to.Items.SingleOrDefault(x => x.AssetId == assetId); var actual = actualItem?.Quantity; var status = expectedItem?.Quantity is null || actual is null ? "Unavailable" : expectedItem.Quantity == actual ? "Match" : "Mismatch"; var reasons = status == "Mismatch" ? ["AnchorQuantityMismatch"] : status == "Unavailable" ? ["CostOrPositionUnavailable"] : Array.Empty<string>();
                result.Add(new(from.Id, to.Id, "Position", assetId, status, expectedItem?.Quantity, actual, expectedItem?.Quantity is { } expectedQuantity && actual is { } actualQuantity ? expectedQuantity - actualQuantity : null, reasons));
                if (expectedItem is { AverageCost: { } expectedCost } && expectedItem.Quantity == actual && actualItem?.AverageCost is { } actualCost && Math.Abs(expectedCost - actualCost) > 0.01m) result.Add(new(from.Id, to.Id, "CostBasis", assetId, "Mismatch", expectedCost, actualCost, expectedCost - actualCost, ["AnchorCostBasisMismatch"]));
            }
        }
        var cashAnchors = await db.PortfolioCashAnchors.AsNoTracking().Where(x => x.PortfolioId == portfolioId).OrderBy(x => x.AnchorDate).ToListAsync(ct);
        for (var i = 1; i < cashAnchors.Count; i++)
        {
            var from = cashAnchors[i - 1]; var to = cashAnchors[i]; var expected = await cash.Get(portfolioId, to.AnchorDate, ct, from.Id); var status = expected?.Balance is null ? "Unavailable" : expected.Balance == to.Balance ? "Match" : "Mismatch";
            result.Add(new(from.Id, to.Id, "Cash", null, status, expected?.Balance, to.Balance, expected?.Balance - to.Balance, status == "Mismatch" ? ["CashAnchorContinuityMismatch"] : []));
        }
        return result;
    }

    private async Task<DimensionResult> AssetCoverage(Guid portfolioId, FinancialHistoryCoverageDimension dimension, DateOnly from, DateOnly to, IEnumerable<Guid> assetIds, CancellationToken ct)
    {
        var values = await Task.WhenAll(assetIds.Select(id => coverage.Resolve(portfolioId, dimension, from, to, id, ct)));
        return Combine(values.Select(x => new DimensionResult(x.Status, x.Reasons, x.Status == "Complete" ? from : null)).ToList(), false, null);
    }

    private async Task<DimensionResult> PortfolioCoverage(Guid portfolioId, FinancialHistoryCoverageDimension dimension, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var value = await coverage.Resolve(portfolioId, dimension, from, to, null, ct);
        return new(value.Status, value.Reasons, value.Status == "Complete" ? from : null);
    }

    private async Task<DimensionResult> PriceCoverage(Guid portfolioId, DateOnly to, DateOnly from, CancellationToken ct)
    {
        if (from > to) return new("Unavailable", ["InvalidPriceInterval"], null);
        var dates = await db.AssetPrices.AsNoTracking().Where(x => x.PriceDate >= from && x.PriceDate <= to).Select(x => x.PriceDate).Distinct().ToListAsync(ct);
        dates.Add(from); dates.Add(to);
        var prices = await db.AssetPrices.AsNoTracking().Where(x => x.PriceDate <= to).Select(x => new { x.AssetId, x.PriceDate }).ToListAsync(ct);
        var gaps = new List<string>();
        foreach (var date in dates.Distinct().OrderBy(x => x))
        {
            var point = await state.Get(portfolioId, date, null, ct);
            foreach (var id in point?.Assets.Where(x => x.Quantity is > 0).Select(x => x.AssetId).Distinct() ?? [])
                if (!prices.Any(x => x.AssetId == id && x.PriceDate <= date)) gaps.Add($"MissingMarketPrice:{id}:{date:yyyy-MM-dd}");
        }
        return gaps.Count == 0 ? new("Complete", [], from) : new("Partial", gaps.Distinct().ToList(), null);
    }

    private static DimensionResult Combine(IReadOnlyList<DimensionResult> values, bool mismatch, string? mismatchReason)
    {
        var reasons = values.SelectMany(x => x.Reasons).ToList(); if (mismatch) reasons.Add(mismatchReason!);
        var status = mismatch ? "Partial" : values.Count > 0 && values.All(x => x.Status == "Complete") ? "Complete" : values.Any(x => x.Status != "Unavailable") ? "Partial" : "Unavailable";
        return new(status, reasons.Distinct().ToList(), status == "Complete" ? values.Max(x => x.ReliableFrom) : null);
    }

    private static DimensionResult Degrade(DimensionResult value, string reason) => value.Status == "Unavailable" ? value : new("Partial", value.Reasons.Concat([reason]).Distinct().ToList(), null);

    private static HistoricalCompletenessDimensionDto Dimension(string name, DimensionResult value) => new(name, value.Status, value.ReliableFrom, null, value.Reasons, [], value.Reasons.Count.ToString());
    private static bool IsDateWarning(string warning) => warning.Contains("Approximate", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<MetricReadinessDto> Metrics(IReadOnlyList<HistoricalCompletenessDimensionDto> d, HistoricalPortfolioStateDto state, HistoricalCashStateDto? cash, DimensionResult transactions, DimensionResult cashFlows, DimensionResult income)
    {
        var pos = Status(d, "Position"); var cost = Status(d, "CostBasis"); var prices = Status(d, "MarketPrices"); var cashStatus = Status(d, "Cash"); var date = Status(d, "DatePrecision"); var invested = Ready(pos, prices); var total = Ready(invested, cashStatus); var grossGain = Ready(total, cashFlows.Status);
        return [Metric("HistoricalInvestedValuation", invested, "InvestedAssets"), Metric("TotalPortfolioValuation", total, "TotalPortfolio"), Metric("HistoricalUnrealizedPnl", Ready(pos, cost, prices), "Gross"), Metric("HistoricalRealizedPnl", Ready(transactions.Status, cost, date), "Gross"), Metric("ValueChange", total, "TotalPortfolio"), Metric("NetExternalFlows", Ready(cashFlows.Status, date), "Gross"), Metric("RecordedIncome", income.Status == "Unavailable" ? "Blocked" : "Ready", "RecordedOnly"), Metric("GrossPortfolioGain", Ready(grossGain, cashFlows.Status), "Gross"), Metric("GrossTotalPortfolioReturn", "Blocked", "Gross"), Metric("TWR", "Blocked", "Gross"), Metric("XIRR", "Blocked", "Gross"), Metric("Attribution", "Blocked", "Gross"), Metric("NetPortfolioReturn", "Blocked", "Net")];
    }

    private static string Status(IReadOnlyList<HistoricalCompletenessDimensionDto> values, string name) => values.Single(x => x.Name == name).Status;
    private static string Ready(params string[] values) => values.All(x => x == "Complete" || x == "Ready") ? "Ready" : values.Any(x => x != "Unavailable") ? "Partial" : "Blocked";
    private static MetricReadinessDto Metric(string name, string status, string scope) => new(name, status, null, scope, []);
}

public sealed record PortfolioHistoricalCompletenessDto(Guid PortfolioId, string OverallStatus, DateOnly? ReliableFrom, IReadOnlyList<HistoricalCompletenessDimensionDto> Dimensions, IReadOnlyList<HistoricalContinuityCheckDto> Continuity, IReadOnlyList<string> BlockingGaps, IReadOnlyList<string> Warnings, IReadOnlyList<MetricReadinessDto> MetricReadiness);
public sealed record HistoricalCompletenessDimensionDto(string Name, string Status, DateOnly? ReliableFrom, DateOnly? ReliableTo, IReadOnlyList<string> Reasons, IReadOnlyList<string> Warnings, string EvidenceSummary);
public sealed record HistoricalContinuityCheckDto(Guid FromAnchorId, Guid ToAnchorId, string Dimension, Guid? AssetId, string Status, decimal? Expected, decimal? Actual, decimal? Delta, IReadOnlyList<string> Reasons);
public sealed record MetricReadinessDto(string Name, string Status, DateOnly? ReliableFrom, string Scope, IReadOnlyList<string> Reasons);
internal sealed record DimensionResult(string Status, IReadOnlyList<string> Reasons, DateOnly? ReliableFrom);
