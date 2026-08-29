using Lodira.Application.Portfolio;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
namespace Lodira.Application.Contributions;
public sealed record GuidancePosition(Guid AssetId, string Ticker, AssetClass AssetClass, decimal CurrentValue, decimal CurrentWeight);
public sealed record GuidanceAssessment(Guid AssetId, CapitalDecisionImpact Impact, AssessmentStatus Status, string? Evidence, string AssessmentVersion);
public sealed record ContributionGuidanceInput(Guid PortfolioId, decimal ContributionAmount, PortfolioHealthResult Health, IReadOnlyList<GuidancePosition> Positions, IReadOnlyList<GuidanceAssessment> Assessments);
public sealed record ContributionAllocation(Guid AssetId, string Ticker, AssetClass AssetClass, decimal Amount, string Reason, IReadOnlyList<string> Evidence);
public sealed record AvoidedCandidate(Guid AssetId, string Ticker, AssetClass AssetClass, ContributionAvoidanceReasonType ReasonType, string Reason, IReadOnlyList<string> Evidence);
public sealed record ContributionGuidance(ContributionGuidanceStatus Status, Guid PortfolioId, decimal ContributionAmount, IReadOnlyList<ContributionAllocation> Allocations, IReadOnlyList<AvoidedCandidate> AvoidedCandidates, decimal UnallocatedAmount, ContributionAllocationStyle AllocationStyle, IReadOnlyList<string> Reasons, IReadOnlyList<string> Evidence, string PortfolioHealthPolicyVersion, IReadOnlyList<string> AssessmentVersions, DateTimeOffset GeneratedAt)
{
    public decimal RequestedAmount => ContributionAmount; public decimal AllocatedAmount => Allocations.Sum(x => x.Amount);
}
public sealed class ContributionGuidanceCalculator
{
    public ContributionGuidance Calculate(ContributionGuidanceInput input)
    {
        if (input.PortfolioId == Guid.Empty) throw new ArgumentException("A carteira é obrigatória.", nameof(input));
        if (input.ContributionAmount <= 0) throw new ArgumentOutOfRangeException(nameof(input.ContributionAmount));
        var amount = Money(input.ContributionAmount); var positions = input.Positions.Where(p => p.CurrentValue >= 0).OrderBy(p => p.Ticker, StringComparer.Ordinal).ThenBy(p => p.AssetId).ToList();
        if (positions.Count == 0) return Empty(input, ContributionGuidanceStatus.EmptyPortfolio, "A carteira não possui posições existentes elegíveis.");
        if (input.Health.CapitalNeeds.Count == 0) return Empty(input, ContributionGuidanceStatus.NoNeeds, "A carteira está alinhada segundo as alocações-alvo; não há classe com necessidade matemática.");
        var impacts = input.Assessments.Where(a => a.Status != AssessmentStatus.Closed).GroupBy(a => a.AssetId).ToDictionary(g => g.Key, EffectiveImpact);
        var allocations = new Dictionary<Guid, decimal>(); var avoided = new List<AvoidedCandidate>(); var reasons = new List<string>(); var remaining = amount;
        foreach (var need in input.Health.CapitalNeeds.OrderBy(n => n.PriorityOrder))
        {
            if (remaining <= 0) break;
            var candidates = positions.Where(p => p.AssetClass == need.AssetClass).Select(p => (Position:p, Impact:impacts.GetValueOrDefault(p.AssetId))).ToList();
            foreach (var c in candidates.Where(c => c.Impact == CapitalDecisionImpact.Block)) avoided.Add(new(c.Position.AssetId,c.Position.Ticker,c.Position.AssetClass,ContributionAvoidanceReasonType.CapitalDecisionBlock,"Evitar aumentar exposição agora: há assessment ativo com impacto Block.",EvidenceFor(c.Position.AssetId,input.Assessments)));
            var eligible = candidates.Where(c => c.Impact != CapitalDecisionImpact.Block).OrderBy(c => c.Impact == CapitalDecisionImpact.Favor ? 0 : c.Impact == CapitalDecisionImpact.None ? 1 : 2).ThenBy(c => c.Position.CurrentWeight).ThenBy(c => c.Position.Ticker,StringComparer.Ordinal).ThenBy(c => c.Position.AssetId).ToList();
            if (eligible.Count == 0) continue;
            foreach (var c in eligible)
            {
                if (remaining <= 0) break;
                var allocation = remaining; allocations[c.Position.AssetId] = allocations.GetValueOrDefault(c.Position.AssetId) + allocation; remaining = 0;
                reasons.Add($"{need.AssetClass} é uma necessidade matemática e {c.Position.Ticker} foi selecionado pelo ranking determinístico.");
            }
            foreach (var c in eligible.Skip(1)) if (!allocations.ContainsKey(c.Position.AssetId)) avoided.Add(new(c.Position.AssetId,c.Position.Ticker,c.Position.AssetClass,ContributionAvoidanceReasonType.LowerPriority,"Foi preterido por outro candidato elegível da mesma classe.",Array.Empty<string>()));
        }
        var allocated = allocations.Where(x=>x.Value>0).OrderBy(x=>x.Key).Select(x=>{var p=positions.Single(y=>y.AssetId==x.Key); var impact=impacts.GetValueOrDefault(x.Key); return new ContributionAllocation(x.Key,p.Ticker,p.AssetClass,Money(x.Value),impact==CapitalDecisionImpact.Caution?"Posição existente elegível, com cautela ativa.":"Posição existente priorizada dentro de uma classe necessária.",EvidenceFor(x.Key,input.Assessments).Concat(new[]{ $"Necessidade calculada pela PortfolioHealth policy {input.Health.PolicyVersion}."}).Distinct().ToList());}).ToList();
        var unallocated=Money(remaining); if(unallocated>0) reasons.Add($"{unallocated:0.00} permanece sem alocação porque não há candidatos elegíveis para as necessidades atuais.");
        var style=allocated.Count switch {0=>ContributionAllocationStyle.NoAllocation,1=>ContributionAllocationStyle.Concentrated,_=>ContributionAllocationStyle.Distributed};
        return new(allocated.Count==0?ContributionGuidanceStatus.NoEligibleCandidates:ContributionGuidanceStatus.Calculated,input.PortfolioId,amount,allocated,avoided.GroupBy(x=>x.AssetId).Select(g=>g.First()).ToList(),unallocated,style,reasons.Distinct().ToList(),input.Assessments.SelectMany(a=>a.Evidence is null?Array.Empty<string>():new[]{a.Evidence}).Distinct().ToList(),input.Health.PolicyVersion,input.Assessments.Select(a=>a.AssessmentVersion).Distinct().ToList(),DateTimeOffset.UtcNow);
    }
    private static CapitalDecisionImpact EffectiveImpact(IEnumerable<GuidanceAssessment> x){var s=x.Select(a=>a.Impact).ToHashSet();return s.Contains(CapitalDecisionImpact.Block)?CapitalDecisionImpact.Block:s.Contains(CapitalDecisionImpact.Caution)?CapitalDecisionImpact.Caution:s.Contains(CapitalDecisionImpact.Favor)?CapitalDecisionImpact.Favor:CapitalDecisionImpact.None;}
    private static ContributionGuidance Empty(ContributionGuidanceInput i,ContributionGuidanceStatus s,string reason)=>new(s,i.PortfolioId,Money(i.ContributionAmount),Array.Empty<ContributionAllocation>(),Array.Empty<AvoidedCandidate>(),Money(i.ContributionAmount),ContributionAllocationStyle.NoAllocation,new[]{reason},Array.Empty<string>(),i.Health.PolicyVersion,Array.Empty<string>(),DateTimeOffset.UtcNow);
    private static decimal Money(decimal v)=>Math.Round(v,2,MidpointRounding.AwayFromZero);
    private static IReadOnlyList<string> EvidenceFor(Guid id,IEnumerable<GuidanceAssessment> a)=>a.Where(x=>x.AssetId==id).Select(x=>x.Evidence).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x!).Distinct(StringComparer.Ordinal).ToList();
}
