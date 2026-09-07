using Prumo.Application.Portfolio;
using Prumo.Application.Abstractions;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Application.Contributions;

public sealed record GuidancePosition(
    Guid AssetId,
    string Ticker,
    AssetClass AssetClass,
    decimal CurrentValue,
    decimal CurrentWeight,
    AssetType AssetType = AssetType.Other,
    string? Sector = null,
    decimal RecentContributionAmount = 0,
    int RecentContributionCount = 0,
    DateTimeOffset? LastContributionAt = null,
    decimal SectorWeightWithinClass = 100,
    bool HasPosition = true,
    decimal AllocationNeedScore = 0,
    decimal DiversificationScore = 0,
    decimal UnderweightScore = 0,
    decimal RecentContributionPenalty = 0,
    EconomicSector? SectorCategory = null,
    ExposureCoverage GeographicExposureCoverage = ExposureCoverage.Unavailable,
    IReadOnlyList<KnownGeographicExposure>? GeographicExposures = null,
    ExposureCoverage EconomicCurrencyExposureCoverage = ExposureCoverage.Unavailable,
    IReadOnlyList<KnownCurrencyExposure>? CurrencyExposures = null,
    string? ListingCountryCode = null,
    string? TradingCurrency = null,
    CandidateFinancialSnapshot? FinancialSnapshot = null)
{
    public bool IsEstablishingPosition { get; init; }
    public decimal DecisionScore => AllocationNeedScore * 0.25m
        + (DiversificationScore > 0 ? DiversificationScore : Math.Clamp(100m - SectorWeightWithinClass, 0, 100)) * 0.20m
        + UnderweightScore * 0.15m
        + (100m - ContributionRecency.Penalty(RecentContributionPenalty, RecentContributionCount, LastContributionAt, IsEstablishingPosition, CurrentWeight)) * 0.15m;
}
public sealed record GuidanceAssessment(Guid AssetId, CapitalDecisionImpact Impact, AssessmentStatus Status, string? Evidence, string AssessmentVersion, AttentionPriority AttentionPriority = AttentionPriority.Low, ActionRequirement ActionRequirement = ActionRequirement.None, string Summary = "", string WhyItMatters = "", string SuggestedBehavior = "", decimal Confidence = 0, Guid AssessmentId = default);
public sealed record ContributionGuidanceInput(Guid PortfolioId, decimal ContributionAmount, PortfolioHealthResult Health, IReadOnlyList<GuidancePosition> Positions, IReadOnlyList<GuidanceAssessment> Assessments);
public sealed record ContributionAllocation(Guid? AssetId, string Ticker, AssetClass AssetClass, decimal Amount, string Reason, IReadOnlyList<string> Evidence, CandidateIdentity? CandidateIdentity = null)
{
    public CandidateIdentity Identity => CandidateIdentity ?? Prumo.Application.Abstractions.CandidateIdentity.Known(AssetId!.Value);
}
public sealed record AvoidedCandidate(Guid AssetId, string Ticker, AssetClass AssetClass, ContributionAvoidanceReasonType ReasonType, string Reason, IReadOnlyList<string> Evidence);
public sealed record ContributionGuidance(ContributionGuidanceStatus Status, Guid PortfolioId, decimal ContributionAmount, IReadOnlyList<ContributionAllocation> Allocations, IReadOnlyList<AvoidedCandidate> AvoidedCandidates, decimal UnallocatedAmount, ContributionAllocationStyle AllocationStyle, IReadOnlyList<string> Reasons, IReadOnlyList<string> Evidence, string PortfolioHealthPolicyVersion, IReadOnlyList<string> AssessmentVersions, DateTimeOffset GeneratedAt)
{
    public decimal RequestedAmount => ContributionAmount;
    public decimal AllocatedAmount => Allocations.Sum(x => x.Amount) + ClassAllocations.Sum(x => x.Amount);
    public IReadOnlyList<ContributionClassAllocation> ClassAllocations { get; init; } = Array.Empty<ContributionClassAllocation>();
}
public sealed record ContributionClassAllocation(AssetClass AssetClass, decimal Amount, string Reason);

public sealed class ContributionGuidanceCalculator
{
    public ContributionGuidance Calculate(ContributionGuidanceInput input)
    {
        if (input.PortfolioId == Guid.Empty) throw new ArgumentException("A carteira é obrigatória.", nameof(input.PortfolioId));
        if (input.ContributionAmount <= 0) throw new ArgumentOutOfRangeException(nameof(input.ContributionAmount));

        var amount = Money(input.ContributionAmount);
        var positions = input.Positions.Where(p => p.CurrentValue >= 0).OrderBy(p => p.Ticker, StringComparer.Ordinal).ThenBy(p => p.AssetId).ToList();
        if (positions.Count == 0) return Empty(input, ContributionGuidanceStatus.EmptyPortfolio, "A carteira não possui posições existentes elegíveis.");

        var targetDeviations = input.Health.AllocationDeviations.GroupBy(x => x.AssetClass).ToDictionary(x => x.Key, x => x.First());
        if (targetDeviations.Count == 0) return Empty(input, ContributionGuidanceStatus.NoNeeds, "Não há alocações-alvo válidas para orientar este aporte.");

        var impacts = input.Assessments.Where(a => a.Status != AssessmentStatus.Closed).GroupBy(a => a.AssetId).ToDictionary(g => g.Key, EffectiveImpact);
        var avoided = new List<AvoidedCandidate>();
        var reasons = new List<string>();
        var allocationsByAsset = new Dictionary<Guid, decimal>();
        var classAllocations = new Dictionary<AssetClass, decimal>();
        var classAllocated = new Dictionary<AssetClass, decimal>();
        var total = positions.Sum(x => x.CurrentValue);
        var projectedTotal = total + amount;
        var remaining = amount;

        while (remaining > 0)
        {
            var candidatesByClass = positions.GroupBy(x => x.AssetClass).ToDictionary(group => group.Key, group => RankedCandidates(group, impacts));
            var needs = targetDeviations.Values
                .Select(deviation => new
                {
                    deviation.AssetClass,
                    Need = Math.Max(0m, projectedTotal * deviation.TargetPercentage / 100m
                        - positions.Where(p => p.AssetClass == deviation.AssetClass).Sum(p => p.CurrentValue)
                        - classAllocated.GetValueOrDefault(deviation.AssetClass))
                })
                .Where(x => x.Need > 0)
                .OrderByDescending(x => x.Need)
                .ThenBy(x => x.AssetClass.ToString(), StringComparer.Ordinal)
                .ToList();

            var selectedNeed = needs.FirstOrDefault(x => candidatesByClass.TryGetValue(x.AssetClass, out var candidates) && candidates.Count > 0 || positions.All(p => p.AssetClass != x.AssetClass));
            if (selectedNeed is null) break;

            var allocation = Math.Min(remaining, selectedNeed.Need);
            if (!candidatesByClass.TryGetValue(selectedNeed.AssetClass, out var candidates) || candidates.Count == 0)
            {
                if (positions.Any(x => x.AssetClass == selectedNeed.AssetClass)) break;
                classAllocations[selectedNeed.AssetClass] = classAllocations.GetValueOrDefault(selectedNeed.AssetClass) + allocation;
                remaining = Money(remaining - allocation);
                reasons.Add($"{ContributionText.AssetClassLabel(selectedNeed.AssetClass)} recebe uma reserva de {allocation:0.00} porque ainda não há um ativo cadastrado nessa classe para indicar.");
                continue;
            }

            var selected = candidates[0];
            allocationsByAsset[selected.AssetId] = allocationsByAsset.GetValueOrDefault(selected.AssetId) + allocation;
            classAllocated[selected.AssetClass] = classAllocated.GetValueOrDefault(selected.AssetClass) + allocation;
            remaining = Money(remaining - allocation);
            reasons.Add(string.Format("{0} recebe capital pela necessidade projetada após o novo aporte. {1} foi priorizado pelo ranking determinístico, que considera necessidade de alocação, diversificação, peso atual e aportes recentes.", ContributionText.AssetClassLabel(selected.AssetClass), selected.Ticker));
        }

        foreach (var group in positions.GroupBy(x => x.AssetClass))
        {
            var ranked = RankedCandidates(group, impacts);
            foreach (var candidate in group.Where(x => impacts.GetValueOrDefault(x.AssetId) == CapitalDecisionImpact.Block))
                avoided.Add(Blocked(candidate, input.Assessments));
            foreach (var candidate in ranked.Skip(1).Where(x => !allocationsByAsset.ContainsKey(x.AssetId)))
                avoided.Add(new(candidate.AssetId, candidate.Ticker, candidate.AssetClass, ContributionAvoidanceReasonType.LowerPriority,
                    candidate.RecentContributionCount > 0
                        ? $"Foi adiado porque recebeu {candidate.RecentContributionCount} aporte(s) recentemente; outros candidatos elegíveis podem melhorar mais a carteira neste momento."
                        : "Não foi priorizado porque outro ativo elegível da mesma classe apresentou maior benefício calculado para reduzir o desvio da carteira.", EvidenceFor(candidate.AssetId, input.Assessments)));
        }

        var allocated = allocationsByAsset.Where(x => x.Value > 0).OrderBy(x => x.Key).Select(x =>
        {
            var position = positions.Single(y => y.AssetId == x.Key);
            var impact = impacts.GetValueOrDefault(x.Key);
            var evidence = EvidenceFor(x.Key, input.Assessments)
                .Concat(new[] { string.Format("Necessidade calculada pela projeção da PortfolioHealth policy {0}.", input.Health.PolicyVersion) })
                .Distinct(StringComparer.Ordinal).ToList();
            var reason = impact == CapitalDecisionImpact.Caution
                ? "Posição existente elegível, com cautela ativa; a seleção decorre do cálculo de necessidade e diversificação, não de uma promessa de retorno."
                : "Posição existente priorizada dentro de uma classe com necessidade projetada, considerando peso atual, diversificação e benefício marginal calculado.";
            return new ContributionAllocation(x.Key, position.Ticker, position.AssetClass, Money(x.Value), reason, evidence);
        }).ToList();

        var unallocated = Money(remaining);
        if (unallocated > 0) reasons.Add(string.Format("{0:0.00} permanece sem alocação porque não há candidatos elegíveis para as necessidades projetadas.", unallocated));
        var allocationCount = allocated.Count + classAllocations.Count;
        var style = allocationCount switch { 0 => ContributionAllocationStyle.NoAllocation, 1 => ContributionAllocationStyle.Concentrated, _ => ContributionAllocationStyle.Distributed };
        return new(
            allocationCount == 0 ? ContributionGuidanceStatus.NoEligibleCandidates : ContributionGuidanceStatus.Calculated,
            input.PortfolioId, amount, allocated, avoided.GroupBy(x => x.AssetId).Select(g => g.First()).ToList(), unallocated, style,
            reasons.Distinct().ToList(),
            input.Assessments.SelectMany(a => a.Evidence is null ? Array.Empty<string>() : new[] { a.Evidence }).Distinct().ToList(),
            input.Health.PolicyVersion, input.Assessments.Select(a => a.AssessmentVersion).Distinct().ToList(), DateTimeOffset.UtcNow)
        {
            ClassAllocations = classAllocations.Select(x => new ContributionClassAllocation(x.Key, Money(x.Value), "Reserva de classe sem ativo cadastrado para indicação.")).ToArray()
        };
    }

    private static List<GuidancePosition> RankedCandidates(IEnumerable<GuidancePosition> positions, IReadOnlyDictionary<Guid, CapitalDecisionImpact> impacts) => positions
        .Where(p => impacts.GetValueOrDefault(p.AssetId) != CapitalDecisionImpact.Block)
        .OrderBy(p => impacts.GetValueOrDefault(p.AssetId) == CapitalDecisionImpact.Favor ? 0 : impacts.GetValueOrDefault(p.AssetId) == CapitalDecisionImpact.None ? 1 : 2)
        .ThenByDescending(p => p.DecisionScore)
        .ThenBy(p => p.CurrentWeight).ThenBy(p => p.Ticker, StringComparer.Ordinal).ThenBy(p => p.AssetId).ToList();

    private static AvoidedCandidate Blocked(GuidancePosition position, IEnumerable<GuidanceAssessment> assessments) => new(
        position.AssetId, position.Ticker, position.AssetClass, ContributionAvoidanceReasonType.CapitalDecisionBlock,
        "Evitar aumentar exposição agora: há assessment ativo com impacto Block.", EvidenceFor(position.AssetId, assessments));

    private static CapitalDecisionImpact EffectiveImpact(IEnumerable<GuidanceAssessment> assessments)
    {
        var values = assessments.Select(a => a.Impact).ToHashSet();
        return values.Contains(CapitalDecisionImpact.Block) ? CapitalDecisionImpact.Block
            : values.Contains(CapitalDecisionImpact.Caution) ? CapitalDecisionImpact.Caution
            : values.Contains(CapitalDecisionImpact.Favor) ? CapitalDecisionImpact.Favor
            : CapitalDecisionImpact.None;
    }

    private static ContributionGuidance Empty(ContributionGuidanceInput input, ContributionGuidanceStatus status, string reason) => new(
        status, input.PortfolioId, Money(input.ContributionAmount), Array.Empty<ContributionAllocation>(), Array.Empty<AvoidedCandidate>(),
        Money(input.ContributionAmount), ContributionAllocationStyle.NoAllocation, new[] { reason }, Array.Empty<string>(),
        input.Health.PolicyVersion, Array.Empty<string>(), DateTimeOffset.UtcNow);

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static IReadOnlyList<string> EvidenceFor(Guid id, IEnumerable<GuidanceAssessment> assessments) => assessments
        .Where(x => x.AssetId == id).Select(x => x.Evidence).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!)
        .Distinct(StringComparer.Ordinal).ToList();
}
