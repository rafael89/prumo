using Prumo.Domain.Enums;

namespace Prumo.Application.Contributions;

public enum PortfolioDecisionType
{
    ReinforceExisting,
    AddNewAsset,
    SplitContribution,
    NoAction
}

public sealed record PortfolioNeed(AssetClass AssetClass, decimal CurrentPercentage, decimal TargetPercentage, decimal GapPercentagePoints);
public sealed record ContributionScenario(IReadOnlyList<(Guid AssetId, decimal Amount)> Allocations, decimal DeviationBefore, decimal DeviationAfter, decimal Improvement, IReadOnlyList<string> Reasons);
public sealed record PortfolioDecision(PortfolioDecisionType DecisionType, decimal ContributionAmount, PortfolioNeed? PrimaryNeed, ContributionScenario? RecommendedScenario, IReadOnlyList<ContributionScenario> AlternativeScenarios, IReadOnlyList<string> Reasons, IReadOnlyList<string> MissingData);

public sealed class PortfolioDecisionService
{
    public PortfolioDecision Decide(decimal contributionAmount, IReadOnlyList<ContributionCandidate> candidates, IReadOnlyList<(Guid AssetId, decimal Amount)> recommendedAllocations)
    {
        candidates ??= Array.Empty<ContributionCandidate>();
        recommendedAllocations ??= Array.Empty<(Guid AssetId, decimal Amount)>();
        var needs = candidates
            .GroupBy(x => x.AssetClass)
            .Select(group =>
            {
                var first = group.First();
                return new PortfolioNeed(group.Key, first.CurrentClassWeight, first.TargetClassWeight, first.TargetClassWeight - first.CurrentClassWeight);
            })
            .Where(x => x.GapPercentagePoints > 2m)
            .OrderByDescending(x => x.GapPercentagePoints)
            .ThenBy(x => x.AssetClass.ToString(), StringComparer.Ordinal)
            .ToArray();

        if (needs.Length == 0) return NoAction(contributionAmount, "Não há uma necessidade de alocação suficientemente relevante agora.");

        var byId = candidates.ToDictionary(x => x.AssetId);
        var allocations = recommendedAllocations.Where(x => x.Amount > 0 && byId.ContainsKey(x.AssetId)).ToArray();
        if (allocations.Length == 0) return NoAction(contributionAmount, "O Prumo não encontrou um candidato elegível para a necessidade atual.");

        var before = ContributionProjectionEvaluator.AggregateClassDeviation(candidates, Array.Empty<(Guid, decimal)>());
        var after = ContributionProjectionEvaluator.AggregateClassDeviation(candidates, allocations);
        var scenario = new ContributionScenario(allocations, before, after, before - after, new[] { $"A classe {needs[0].AssetClass} está {needs[0].GapPercentagePoints:0.##} p.p. abaixo do target." });
        if (scenario.Improvement <= 0.01m) return NoAction(contributionAmount, "O Prumo não encontrou um aporte suficientemente adequado entre os ativos conhecidos e os dados disponíveis agora.");

        var decisionType = allocations.Length > 1 ? PortfolioDecisionType.SplitContribution : byId[allocations[0].AssetId].HasPosition ? PortfolioDecisionType.ReinforceExisting : PortfolioDecisionType.AddNewAsset;
        var alternatives = candidates
            .Where(x => !x.HasBlock && x.AssetClass == needs[0].AssetClass && allocations.All(a => a.AssetId != x.AssetId))
            .OrderByDescending(x => x.DecisionScore)
            .ThenBy(x => x.RecentContributionCount)
            .ThenBy(x => x.Ticker, StringComparer.Ordinal)
            .ThenBy(x => x.AssetId)
            .Take(2)
            .Select(x => Scenario(candidates, new[] { (x.AssetId, contributionAmount) }, $"{x.Ticker} é uma alternativa conhecida para a classe atualmente mais defasada."))
            .Where(x => x.Improvement > 0.01m)
            .ToArray();

        return new(decisionType, contributionAmount, needs[0], scenario, alternatives,
            new[] { "O cenário recomendado produz a melhor melhora mensurável entre os candidatos avaliados." }, Array.Empty<string>());
    }

    private static ContributionScenario Scenario(IReadOnlyList<ContributionCandidate> candidates, IReadOnlyList<(Guid AssetId, decimal Amount)> allocations, string reason)
    {
        var before = ContributionProjectionEvaluator.AggregateClassDeviation(candidates, Array.Empty<(Guid, decimal)>());
        var after = ContributionProjectionEvaluator.AggregateClassDeviation(candidates, allocations);
        return new(allocations, before, after, before - after, new[] { reason });
    }

    private static PortfolioDecision NoAction(decimal amount, string reason) => new(PortfolioDecisionType.NoAction, amount, null, null, Array.Empty<ContributionScenario>(), new[] { reason }, Array.Empty<string>());
}
