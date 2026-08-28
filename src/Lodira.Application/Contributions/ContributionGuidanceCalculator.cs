using Lodira.Domain.Enums;

namespace Lodira.Application.Contributions;

public sealed record ContributionPosition(Guid AssetId, string Ticker, decimal CurrentValue, decimal TargetWeight, bool ReviewRequired);
public sealed record ContributionGuidanceItem(Guid AssetId, string Ticker, decimal CurrentValue, decimal CurrentWeight, decimal TargetWeight, decimal AllocationGap,
    decimal SuggestedContribution, decimal ProjectedValue, decimal ProjectedWeight, ContributionEligibility Eligibility, IReadOnlyList<ContributionReason> Reasons);
public sealed record ContributionGuidanceResult(decimal RequestedAmount, decimal AllocatedAmount, decimal UnallocatedAmount, IReadOnlyList<ContributionGuidanceItem> Items);

public sealed class ContributionGuidanceCalculator
{
    public const int MaxSuggestedAssets = 3;

    public ContributionGuidanceResult Calculate(IEnumerable<ContributionPosition> positions, decimal requestedAmount)
    {
        if (requestedAmount <= 0 || requestedAmount > 1_000_000_000m) throw new ArgumentOutOfRangeException(nameof(requestedAmount));
        var list = positions.OrderBy(p => p.AssetId).ToList();
        var total = list.Sum(p => p.CurrentValue);
        var projectedTotal = total + requestedAmount;
        var deficits = list.Select(p => Math.Max(0m, projectedTotal * p.TargetWeight / 100m - p.CurrentValue)).ToList();
        var selectedIndices = deficits.Select((deficit, index) => (deficit, index))
            .Where(item => item.deficit > 0)
            .OrderByDescending(item => item.deficit)
            .ThenBy(item => list[item.index].AssetId)
            .Take(MaxSuggestedAssets)
            .Select(item => item.index)
            .ToHashSet();
        var eligibleDeficit = selectedIndices.Where(i => !list[i].ReviewRequired).Sum(i => deficits[i]);
        var allocated = Math.Min(requestedAmount, eligibleDeficit);
        var raw = deficits.Select((d, i) => !selectedIndices.Contains(i) || list[i].ReviewRequired || eligibleDeficit == 0 ? 0 : d / eligibleDeficit * allocated).ToList();
        var cents = raw.Select(v => Math.Floor(v * 100m) / 100m).ToList();
        var remainingCents = (int)Math.Round((allocated - cents.Sum()) * 100m, 0, MidpointRounding.AwayFromZero);
        foreach (var index in raw.Select((value, i) => (value, i)).Where(item => item.value > 0).OrderByDescending(item => item.value - cents[item.i]).ThenBy(item => list[item.i].AssetId).Take(remainingCents).Select(item => item.i)) cents[index] += .01m;

        var items = list.Select((p, i) => (p, i)).Where(item => selectedIndices.Contains(item.i)).Select(item => {
            var p = item.p;
            var i = item.i;
            var reasons = new List<ContributionReason>();
            if (p.CurrentValue > total * p.TargetWeight / 100m && total > 0) reasons.Add(ContributionReason.AboveTarget);
            else if (deficits[i] > 0) reasons.Add(ContributionReason.BelowTarget);
            else reasons.Add(ContributionReason.AtOrAboveTarget);
            if (p.ReviewRequired) reasons.Add(ContributionReason.PendingRelevantRadarEvent);
            else if (cents[i] > 0) reasons.Add(ContributionReason.EligibleForContribution);
            return new ContributionGuidanceItem(p.AssetId, p.Ticker, p.CurrentValue, total == 0 ? 0 : p.CurrentValue / total * 100m, p.TargetWeight,
                deficits[i], cents[i], p.CurrentValue + cents[i], projectedTotal == 0 ? 0 : (p.CurrentValue + cents[i]) / projectedTotal * 100m,
                p.ReviewRequired ? ContributionEligibility.ReviewRequired : ContributionEligibility.Eligible, reasons);
        }).ToList();
        return new(requestedAmount, cents.Sum(), requestedAmount - cents.Sum(), items);
    }
}
