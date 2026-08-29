using Lodira.Application.Portfolio;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;

namespace Lodira.Application.Contributions;

public sealed record GuidancePosition(Guid AssetId, string Ticker, AssetClass AssetClass, decimal CurrentValue, decimal CurrentWeight);
public sealed record ContributionPosition(Guid AssetId, string Ticker, decimal CurrentValue, decimal TargetWeight, bool ReviewRequired);
public sealed record ContributionGuidanceItem(Guid AssetId, string Ticker, decimal CurrentValue, decimal CurrentWeight, decimal TargetWeight, decimal AllocationGap, decimal SuggestedContribution, decimal ProjectedValue, decimal ProjectedWeight, ContributionEligibility Eligibility, IReadOnlyList<ContributionReason> Reasons);
public sealed record ContributionGuidanceResult(decimal RequestedAmount, decimal AllocatedAmount, decimal UnallocatedAmount, IReadOnlyList<ContributionGuidanceItem> Items);
public sealed record GuidanceAssessment(Guid AssetId, CapitalDecisionImpact Impact, AssessmentStatus Status, string? Evidence, string AssessmentVersion);
public sealed record ContributionGuidanceInput(Guid PortfolioId, decimal ContributionAmount, PortfolioAllocationStrategy Strategy, PortfolioHealthResult Health, IReadOnlyList<GuidancePosition> Positions, IReadOnlyList<GuidanceAssessment> Assessments);
public sealed record ContributionAllocation(Guid AssetId, string Ticker, AssetClass AssetClass, decimal Amount, decimal ProjectedAssetWeight, string Reason, IReadOnlyList<string> Evidence);
public sealed record AvoidedCandidate(Guid AssetId, string Ticker, AssetClass AssetClass, ContributionAvoidanceReasonType ReasonType, string Reason, IReadOnlyList<string> Evidence);
public sealed record ContributionGuidance(ContributionGuidanceStatus Status, Guid PortfolioId, decimal ContributionAmount, IReadOnlyList<ContributionAllocation> Allocations, IReadOnlyList<AvoidedCandidate> AvoidedCandidates, decimal UnallocatedAmount, ContributionAllocationStyle AllocationStyle, IReadOnlyList<string> Reasons, IReadOnlyList<string> Evidence, int StrategyVersion, string PortfolioHealthPolicyVersion, string ContributionPolicyVersion, IReadOnlyList<string> AssessmentVersions, DateTimeOffset GeneratedAt)
{
    public decimal RequestedAmount => ContributionAmount;
    public decimal AllocatedAmount => Allocations.Sum(x => x.Amount);
    public IReadOnlyList<ContributionGuidanceItem> Items => Allocations.Select(x => new ContributionGuidanceItem(x.AssetId, x.Ticker, 0, 0, 0, 0, x.Amount, x.Amount, x.ProjectedAssetWeight, ContributionEligibility.Eligible, Array.Empty<ContributionReason>())).ToList();
}

public sealed class ContributionGuidanceCalculator
{
    public const string ContributionPolicyVersion = "contribution-guidance-v2";

    public ContributionGuidance Calculate(ContributionGuidanceInput input)
    {
        if (input.PortfolioId == Guid.Empty) throw new ArgumentException("A carteira é obrigatória.", nameof(input));
        if (input.ContributionAmount <= 0 || input.ContributionAmount > 1_000_000_000m) throw new ArgumentOutOfRangeException(nameof(input.ContributionAmount));
        var amount = Money(input.ContributionAmount);
        var positions = input.Positions.Where(p => p.CurrentValue >= 0).OrderBy(p => p.Ticker, StringComparer.Ordinal).ThenBy(p => p.AssetId).ToList();
        if (positions.Count == 0) return Empty(input, ContributionGuidanceStatus.EmptyPortfolio, "A carteira não possui posições existentes elegíveis.");
        if (input.Health.CapitalNeeds.Count == 0) return Empty(input, ContributionGuidanceStatus.NoNeeds, "A carteira está alinhada segundo a estratégia atual; não há classe com necessidade matemática.");
        var total = positions.Sum(p => p.CurrentValue);
        var impacts = input.Assessments.Where(a => a.Status != AssessmentStatus.Closed).GroupBy(a => a.AssetId).ToDictionary(g => g.Key, EffectiveImpact);
        var needs = input.Health.CapitalNeeds.ToList();
        var allocations = new Dictionary<Guid, decimal>(); var avoided = new List<AvoidedCandidate>(); var reasons = new List<string>(); var remaining = amount;
        var classOrder = input.Strategy.ContributionPolicy == ContributionPolicy.CorrectImbalancesFirst ? needs.OrderBy(n => n.PriorityOrder).ToList() : needs.OrderByDescending(n => n.DeficitPercentagePoints).ThenBy(n => n.AssetClass).ToList();
        var classCapacities = classOrder.ToDictionary(n => n.AssetClass, n => ClassCapacity(n, total));
        foreach (var need in classOrder)
        {
            if (remaining <= 0) break;
            var candidates = positions.Where(p => p.AssetClass == need.AssetClass).Select(p => (Position: p, Impact: impacts.GetValueOrDefault(p.AssetId, CapitalDecisionImpact.None), Capacity: AssetCapacity(p, input.Strategy.MaximumAssetWeight, total, allocations.GetValueOrDefault(p.AssetId)))).ToList();
            foreach (var c in candidates.Where(c => c.Impact == CapitalDecisionImpact.Block)) avoided.Add(new(c.Position.AssetId, c.Position.Ticker, c.Position.AssetClass, ContributionAvoidanceReasonType.CapitalDecisionBlock, "Evitar aumentar exposição agora: há assessment ativo com impacto Block.", EvidenceFor(c.Position.AssetId, input.Assessments)));
            foreach (var c in candidates.Where(c => c.Impact != CapitalDecisionImpact.Block && c.Capacity <= 0)) avoided.Add(new(c.Position.AssetId, c.Position.Ticker, c.Position.AssetClass, ContributionAvoidanceReasonType.ConcentrationLimit, "Não há capacidade restante dentro do limite máximo de concentração.", Array.Empty<string>()));
            var eligible = candidates.Where(c => c.Impact != CapitalDecisionImpact.Block && c.Capacity > 0).OrderBy(c => c.Impact == CapitalDecisionImpact.Caution ? 1 : 0).ThenBy(c => c.Impact == CapitalDecisionImpact.Favor ? 0 : 1).ThenBy(c => c.Position.CurrentWeight).ThenBy(c => c.Position.Ticker, StringComparer.Ordinal).ThenBy(c => c.Position.AssetId).ToList();
            var classAmount = input.Strategy.ContributionPolicy == ContributionPolicy.CorrectImbalancesFirst ? Math.Min(remaining, classCapacities[need.AssetClass]) : Math.Min(remaining, classCapacities[need.AssetClass] * remaining / Math.Max(1m, classOrder.Sum(x => classCapacities[x.AssetClass])));
            foreach (var c in eligible)
            {
                if (classAmount <= 0 || remaining <= 0) break;
                var allocation = Money(Math.Min(classAmount, Math.Min(remaining, c.Capacity))); if (allocation <= 0) continue;
                allocations[c.Position.AssetId] = allocations.GetValueOrDefault(c.Position.AssetId) + allocation; classAmount -= allocation; remaining -= allocation;
                reasons.Add($"{need.AssetClass} é uma necessidade matemática da carteira e {c.Position.Ticker} permanece elegível.{(c.Impact == CapitalDecisionImpact.Caution ? " Há cautela ativa associada ao ativo." : string.Empty)}");
            }
            foreach (var c in eligible.Where(c => !allocations.ContainsKey(c.Position.AssetId))) avoided.Add(new(c.Position.AssetId, c.Position.Ticker, c.Position.AssetClass, ContributionAvoidanceReasonType.LowerPriority, "Foi preterido por outro candidato elegível da mesma classe.", Array.Empty<string>()));
        }
        var allocated = allocations.Where(a => a.Value > 0).OrderBy(a => a.Key).Select(a =>
        {
            var p = positions.Single(x => x.AssetId == a.Key); var projectedTotal = total + allocations.Values.Sum(); var weight = projectedTotal <= 0 ? 0 : (p.CurrentValue + a.Value) / projectedTotal * 100m; if (input.Strategy.MaximumAssetWeight.HasValue) weight = input.Strategy.MaximumAssetWeight.Value - weight <= 0.000001m ? input.Strategy.MaximumAssetWeight.Value : Math.Min(weight, input.Strategy.MaximumAssetWeight.Value); var impact = impacts.GetValueOrDefault(a.Key, CapitalDecisionImpact.None);
            return new ContributionAllocation(a.Key, p.Ticker, p.AssetClass, Money(a.Value), weight, impact == CapitalDecisionImpact.Caution ? "Posição existente elegível, com cautela ativa explicitada." : "Posição existente priorizada dentro de uma classe necessária.", EvidenceFor(a.Key, input.Assessments).Concat(new[] { $"Necessidade calculada pela PortfolioHealth policy {input.Health.PolicyVersion}.", $"Estratégia v{input.Strategy.Version}." }).Distinct().ToList());
        }).ToList();
        var unallocated = Money(remaining); if (unallocated > 0) reasons.Add($"{unallocated:0.00} permaneceram sem alocação para respeitar as necessidades e restrições atuais.");
        var style = allocated.Count switch { 0 => ContributionAllocationStyle.NoAllocation, 1 => ContributionAllocationStyle.Concentrated, _ => ContributionAllocationStyle.Distributed };
        return new(allocated.Count == 0 ? ContributionGuidanceStatus.NoEligibleCandidates : ContributionGuidanceStatus.Calculated, input.PortfolioId, amount, allocated, avoided.GroupBy(a => a.AssetId).Select(g => g.First()).ToList(), unallocated, style, reasons.Distinct().ToList(), new List<string> { $"PortfolioHealth policy {input.Health.PolicyVersion}.", $"Estratégia v{input.Strategy.Version}." }.Concat(input.Assessments.SelectMany(a => a.Evidence is null ? Array.Empty<string>() : new[] { a.Evidence })).Distinct().ToList(), input.Strategy.Version, input.Health.PolicyVersion, ContributionPolicyVersion, input.Assessments.Where(a => a.Status != AssessmentStatus.Closed).Select(a => a.AssessmentVersion).Distinct().ToList(), DateTimeOffset.UtcNow);
    }

    private static CapitalDecisionImpact EffectiveImpact(IEnumerable<GuidanceAssessment> items) { var set = items.Select(x => x.Impact).ToHashSet(); return set.Contains(CapitalDecisionImpact.Block) ? CapitalDecisionImpact.Block : set.Contains(CapitalDecisionImpact.Caution) ? CapitalDecisionImpact.Caution : set.Contains(CapitalDecisionImpact.Favor) ? CapitalDecisionImpact.Favor : CapitalDecisionImpact.None; }
    private static decimal ClassCapacity(CapitalNeed need, decimal total) { var target = need.TargetPercentage / 100m; var current = need.CurrentPercentage / 100m * total; return target >= 1 ? 0 : Math.Max(0, (target * total - current) / (1 - target)); }
    private static decimal AssetCapacity(GuidancePosition p, decimal? max, decimal total, decimal already) { if (!max.HasValue || max <= 0 || max >= 100) return decimal.MaxValue; var projectedTotal = total + already; var raw = Math.Max(0, (max.Value / 100m * projectedTotal - (p.CurrentValue + already)) / (1 - max.Value / 100m)); return Math.Floor(raw * 100m) / 100m; }
    private static ContributionGuidance Empty(ContributionGuidanceInput input, ContributionGuidanceStatus status, string reason) => new(status, input.PortfolioId, Money(input.ContributionAmount), Array.Empty<ContributionAllocation>(), Array.Empty<AvoidedCandidate>(), Money(input.ContributionAmount), ContributionAllocationStyle.NoAllocation, new[] { reason }, Array.Empty<string>(), input.Strategy.Version, input.Health.PolicyVersion, ContributionPolicyVersion, Array.Empty<string>(), DateTimeOffset.UtcNow);
    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static IReadOnlyList<string> EvidenceFor(Guid assetId, IEnumerable<GuidanceAssessment> assessments)
        => assessments.Where(a => a.AssetId == assetId).Select(a => a.Evidence).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct(StringComparer.Ordinal).ToList();

    // Compatibility overload for pre-R4 internal callers; the public API uses the contextual overload.
    public ContributionGuidanceResult Calculate(IEnumerable<ContributionPosition> positions, decimal requestedAmount)
    {
        if (requestedAmount <= 0) throw new ArgumentOutOfRangeException(nameof(requestedAmount));
        var list = positions.OrderBy(p => p.AssetId).ToList(); var total = list.Sum(p => p.CurrentValue);
        var deficits = list.Select(p => Math.Max(0m, (total + requestedAmount) * p.TargetWeight / 100m - p.CurrentValue)).ToList();
        var selected = deficits.Select((deficit, index) => (deficit, index)).Where(x => x.deficit > 0).OrderByDescending(x => x.deficit).ThenBy(x => list[x.index].AssetId).Take(3).Select(x => x.index).ToHashSet();
        if (list.Count <= 3) selected = Enumerable.Range(0, list.Count).ToHashSet();
        var eligible = selected.Where(i => !list[i].ReviewRequired).ToList(); var sum = eligible.Sum(i => deficits[i]); var allocated = Math.Min(requestedAmount, sum);
        var items = list.Select((p, i) => new ContributionGuidanceItem(p.AssetId, p.Ticker, p.CurrentValue, total == 0 ? 0 : p.CurrentValue / total * 100m, p.TargetWeight, deficits[i], eligible.Contains(i) && sum > 0 ? deficits[i] / sum * allocated : 0, p.CurrentValue, total == 0 ? 0 : p.CurrentValue / total * 100m, p.ReviewRequired ? ContributionEligibility.ReviewRequired : ContributionEligibility.Eligible, p.ReviewRequired ? new[] { ContributionReason.PendingRelevantRadarEvent } : Array.Empty<ContributionReason>())).Select((item, i) => (item, index: i)).Where(x => selected.Contains(x.index)).OrderByDescending(x => deficits[x.index]).ThenBy(x => x.item.AssetId).Select(x => x.item).ToList();
        return new(requestedAmount, allocated, requestedAmount - allocated, items);
    }
}
