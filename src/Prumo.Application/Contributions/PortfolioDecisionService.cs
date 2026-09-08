using Prumo.Application.Abstractions;
using Prumo.Domain.Enums;

namespace Prumo.Application.Contributions;

public enum PortfolioDecisionType { ReinforceExisting, AddNewAsset, SplitContribution, NoAction }
public sealed record PortfolioNeed(AssetClass AssetClass, decimal CurrentPercentage, decimal TargetPercentage, decimal GapPercentagePoints);
public enum CandidateStructuralFit { DirectlyAddressesPrimaryNeed, AddressesPrimaryNeedButWorsensConcentration, AddressesSecondaryNeedOnly, Neutral, StructurallyInferior, InsufficientExposureContext }
public sealed record IntegratedDecisionEvidence(string PrimaryNeed, decimal PrimaryNeedImprovement, CandidateStructuralFit StructuralFit, string ConcentrationEffect, string SecondaryExposureEffect, string QualityContext, string ValuationContext, string RecentContributionContext, string DecisiveFactor, string? AlternativeComparisonReason);
public sealed record ContributionScenario(IReadOnlyList<CandidateAllocation> Allocations, decimal DeviationBefore, decimal DeviationAfter, decimal Improvement, IReadOnlyList<string> Reasons, PortfolioExposureSummary? ExposureAfter = null, IReadOnlyList<PortfolioExposureImpact>? ExposureImpacts = null);
public sealed record PortfolioDecision(PortfolioDecisionType DecisionType, decimal ContributionAmount, PortfolioNeed? PrimaryNeed, ContributionScenario? RecommendedScenario, IReadOnlyList<ContributionScenario> AlternativeScenarios, IReadOnlyList<string> Reasons, IReadOnlyList<string> MissingData, IReadOnlyList<MultidimensionalPortfolioNeed>? SecondaryNeeds = null, PortfolioExposureSummary? ExposureSummary = null, IReadOnlyList<CandidateFinancialEvaluation>? FinancialEvaluations = null, IntegratedDecisionEvidence? DecisionEvidence = null)
{
    public IReadOnlyList<PortfolioStructuralNeed> StructuralNeeds { get; init; } = Array.Empty<PortfolioStructuralNeed>();
}

public sealed class PortfolioDecisionService
{
    public PortfolioDecision DecideDeterministically(decimal amount, IReadOnlyList<ContributionCandidate> candidates, InvestorPolicy? investorPolicy = null)
    {
        candidates ??= Array.Empty<ContributionCandidate>();
        investorPolicy ??= InvestorPolicy.Current;
        var exposure = PortfolioExposureAggregator.Aggregate(candidates);
        var secondary = PortfolioExposureAggregator.DetectNeeds(candidates, exposure);
        var structuralNeeds = PortfolioExposureAggregator.DetectStructuralNeeds(candidates, exposure);
        var financial = candidates.Where(x => x.Identity.AssetId is not null).Select(x => CandidateFinancialEvaluator.Evaluate(x.Identity.AssetId!.Value, x.AssetType, x.FinancialSnapshot)).ToArray();
        var needs = candidates.GroupBy(x => x.AssetClass)
            .Select(g => { var first = g.First(); return new PortfolioNeed(g.Key, first.CurrentClassWeight, first.TargetClassWeight, first.TargetClassWeight - first.CurrentClassWeight); })
            .Where(x => x.GapPercentagePoints > 2m).OrderByDescending(x => x.GapPercentagePoints).ThenBy(x => x.AssetClass.ToString(), StringComparer.Ordinal).ToArray();
        if (needs.Length == 0) return NoAction(amount, "Não há uma necessidade de alocação suficientemente relevante agora.", secondary, exposure, MissingData(exposure), financial) with { StructuralNeeds = structuralNeeds };
        var eligible = candidates.Where(x => x.AssetClass == needs[0].AssetClass && !x.HasBlock).ToArray();
        var dominant = DominantHeldCandidate(eligible);
        var scenarios = eligible.Select(x => Scenario(candidates, new[] { new CandidateAllocation(x.Identity, amount) }, $"{x.Ticker} atende a necessidade primária de {needs[0].AssetClass}.")).ToArray();
        if (scenarios.Length == 0) return NoAction(amount, "Não há candidato elegível para a necessidade atual.", secondary, exposure, MissingData(exposure), financial) with { StructuralNeeds = structuralNeeds };
        if (dominant is not null && eligible.Length > 1)
        {
            scenarios = scenarios.Where(x => x.Allocations.Count != 1 || x.Allocations[0].Identity != dominant.Identity).ToArray();
            scenarios = scenarios.Concat(GenerateSameClassSplitScenarios(amount, candidates, dominant, eligible)).ToArray();
        }
        if (needs.Length > 1 && eligible.Length > 0)
        {
            var secondNeed = needs[1];
            var second = candidates.FirstOrDefault(x => x.AssetClass == secondNeed.AssetClass && !x.HasBlock);
            if (second is not null)
            {
                var firstAmount = Money(amount * needs[0].GapPercentagePoints / (needs[0].GapPercentagePoints + secondNeed.GapPercentagePoints));
                scenarios = scenarios.Concat(new[] { Scenario(candidates, new[]
                {
                    new CandidateAllocation(eligible[0].Identity, firstAmount),
                    new CandidateAllocation(second.Identity, Money(amount - firstAmount))
                }, $"Divide o aporte entre {needs[0].AssetClass} e {secondNeed.AssetClass} proporcionalmente às defasagens projetadas.") }).ToArray();
            }
        }
        // Also evaluate bounded cross-class splits. This keeps the original
        // class-gap baseline while allowing the best proportion to emerge
        // from the projected portfolio instead of assuming 50/50.
        scenarios = scenarios.Concat(GenerateSplitScenarios(amount, candidates, needs)).ToArray();
        var ordered = scenarios.Where(x => x.Allocations.Sum(a => a.Amount) == amount && IsWithinClassBudgets(x, candidates))
            .OrderBy(x => x, ScenarioComparer(candidates, financial, investorPolicy)).ToArray();
        if (ordered.Length == 0)
            return NoAction(amount, "Nenhum cenário respeita o orçamento disponível para as classes atuais.", secondary, exposure, MissingData(exposure), financial) with { StructuralNeeds = structuralNeeds };
        var winner = ordered[0];
        var candidate = candidates.Single(x => x.CandidateKey == winner.Allocations[0].Key);
        var runner = ordered.Skip(1).FirstOrDefault();
        var factor = runner is null ? "PrimaryStructuralImprovement" : ComparisonFactor(candidates, financial, winner, runner);
        var evidence = new IntegratedDecisionEvidence($"{needs[0].AssetClass} abaixo do target em {needs[0].GapPercentagePoints:0.##} p.p.", winner.Improvement,
            WorsensSevereConcentration(candidates, winner.Allocations) ? CandidateStructuralFit.AddressesPrimaryNeedButWorsensConcentration : CandidateStructuralFit.DirectlyAddressesPrimaryNeed,
            WorsensSevereConcentration(candidates, winner.Allocations) ? "Aumenta concentração severa." : "Não aumenta concentração severa.", SecondaryImprovement(winner) > 0 ? "Melhora exposição secundária conhecida." : "Sem melhoria secundária comprovada.",
            financial.FirstOrDefault(x => x.AssetId == candidate.Identity.AssetId)?.Quality.Category ?? "InsufficientData", "NotUsed",
            candidate.RecentContributionCount > 0 ? "Recebeu aporte recentemente; contexto considerado." : "Sem aporte recente relevante.", factor,
            runner is null ? null : $"{candidates.Single(x => x.CandidateKey == runner.Allocations[0].Key).Ticker} perdeu em {factor}.");
        var type = winner.Allocations.Count > 1 ? PortfolioDecisionType.SplitContribution : candidate.HasPosition ? PortfolioDecisionType.ReinforceExisting : PortfolioDecisionType.AddNewAsset;
        return new PortfolioDecision(type, amount, needs[0], winner, runner is null ? Array.Empty<ContributionScenario>() : new[] { runner }, new[] { "O cenário foi selecionado por comparação determinística pós-aporte." }, MissingData(exposure), secondary, exposure, financial, evidence) with { StructuralNeeds = structuralNeeds };
    }

    private static bool IsWithinClassBudgets(ContributionScenario scenario, IReadOnlyList<ContributionCandidate> candidates)
    {
        var byKey = candidates.ToDictionary(x => x.CandidateKey);
        if (scenario.Allocations.Any(allocation => !byKey.ContainsKey(allocation.Key))) return false;
        return scenario.Allocations
            .GroupBy(allocation => byKey[allocation.Key].AssetClass)
            .All(group => group.Sum(allocation => allocation.Amount) <= byKey[group.First().Key].ClassBudget);
    }

    private static int SecondaryImprovement(ContributionScenario scenario) => scenario.ExposureImpacts?.Count(x => x.AfterPercentage < x.BeforePercentage) ?? 0;
    private static int RecentCount(IReadOnlyList<ContributionCandidate> candidates, ContributionScenario scenario) => scenario.Allocations.Sum(a => candidates.Single(x => x.CandidateKey == a.Key).RecentContributionCount);

    private static Comparer<ContributionScenario> ScenarioComparer(IReadOnlyList<ContributionCandidate> candidates, IReadOnlyList<CandidateFinancialEvaluation> financial, InvestorPolicy policy) => Comparer<ContributionScenario>.Create((left, right) => Compare(candidates, financial, left, right, policy));

    private static int Compare(IReadOnlyList<ContributionCandidate> candidates, IReadOnlyList<CandidateFinancialEvaluation> financial, ContributionScenario left, ContributionScenario right, InvestorPolicy policy)
    {
        var result = right.Improvement.CompareTo(left.Improvement);
        if (result != 0) return result;
        result = StructuralExposureImprovement(left).CompareTo(StructuralExposureImprovement(right));
        if (result != 0) return result;
        result = WorsensSevereConcentration(candidates, left.Allocations).CompareTo(WorsensSevereConcentration(candidates, right.Allocations));
        if (result != 0) return result;
        result = SecondaryImprovement(right).CompareTo(SecondaryImprovement(left));
        if (result != 0) return result;
        result = QualityRank(candidates, financial, right).CompareTo(QualityRank(candidates, financial, left));
        if (result != 0) return result;
        result = CompareRelativeValuation(candidates, financial, left, right);
        if (result != 0) return result;
        result = RecentCount(candidates, left).CompareTo(RecentCount(candidates, right));
        if (result != 0) return result;
        if (policy.PreferExistingPositionsWhenEquivalent)
        {
            result = ExistingPositionCount(candidates, right).CompareTo(ExistingPositionCount(candidates, left));
            if (result != 0) return result;
        }
        return string.Compare(string.Join("|", left.Allocations.Select(x => x.Key.Value).OrderBy(x => x, StringComparer.Ordinal)), string.Join("|", right.Allocations.Select(x => x.Key.Value).OrderBy(x => x, StringComparer.Ordinal)), StringComparison.Ordinal);
    }

    private static int ExistingPositionCount(IReadOnlyList<ContributionCandidate> candidates, ContributionScenario scenario)
        => scenario.Allocations.Count(allocation => candidates.Single(x => x.CandidateKey == allocation.Key).HasPosition);

    private static int QualityRank(IReadOnlyList<ContributionCandidate> candidates, IReadOnlyList<CandidateFinancialEvaluation> financial, ContributionScenario scenario) => scenario.Allocations
        .Select(a => candidates.Single(x => x.CandidateKey == a.Key)).Select(x => financial.FirstOrDefault(f => f.AssetId == x.Identity.AssetId)?.Quality)
        .Where(x => x is not null && x.Category is "Acceptable" or "Weak" or "WeakEvidence")
        .Select(x => x!.Category == "Acceptable" ? 2 : 1).DefaultIfEmpty(0).Min();

    private static string ComparisonFactor(IReadOnlyList<ContributionCandidate> candidates, IReadOnlyList<CandidateFinancialEvaluation> financial, ContributionScenario winner, ContributionScenario runner)
    {
        if (winner.Improvement != runner.Improvement) return "PrimaryStructuralImprovement";
        if (StructuralExposureImprovement(winner) != StructuralExposureImprovement(runner)) return "MultidimensionalExposure";
        if (WorsensSevereConcentration(candidates, winner.Allocations) != WorsensSevereConcentration(candidates, runner.Allocations)) return "Concentration";
        if (SecondaryImprovement(winner) != SecondaryImprovement(runner)) return "SecondaryExposure";
        if (QualityRank(candidates, financial, winner) != QualityRank(candidates, financial, runner)) return "FinancialQuality";
        if (CompareRelativeValuation(candidates, financial, winner, runner) != 0) return "RelativeValuation";
        if (RecentCount(candidates, winner) != RecentCount(candidates, runner)) return "RecentContribution";
        return "StableTieBreaker";
    }

    private static decimal StructuralExposureImprovement(ContributionScenario scenario)
        => scenario.ExposureImpacts?.Where(x => x.AfterPercentage < x.BeforePercentage)
            .Sum(x => x.BeforePercentage - x.AfterPercentage) ?? 0m;

    private static int CompareRelativeValuation(
        IReadOnlyList<ContributionCandidate> candidates,
        IReadOnlyList<CandidateFinancialEvaluation> financial,
        ContributionScenario left,
        ContributionScenario right)
    {
        var leftCandidate = candidates.Single(x => x.CandidateKey == left.Allocations[0].Key);
        var rightCandidate = candidates.Single(x => x.CandidateKey == right.Allocations[0].Key);
        if (leftCandidate.AssetClass != rightCandidate.AssetClass
            || leftCandidate.SectorCategory is null
            || leftCandidate.SectorCategory != rightCandidate.SectorCategory)
            return 0;

        var leftValue = RelativeValuationValue(financial.FirstOrDefault(x => x.AssetId == leftCandidate.Identity.AssetId)?.Valuation);
        var rightValue = RelativeValuationValue(financial.FirstOrDefault(x => x.AssetId == rightCandidate.Identity.AssetId)?.Valuation);
        return leftValue.HasValue && rightValue.HasValue ? leftValue.Value.CompareTo(rightValue.Value) : 0;
    }

    private static decimal? RelativeValuationValue(CandidateValuationEvaluation? valuation)
    {
        if (valuation is null || valuation.Metrics.Count == 0 || valuation.Freshness == FinancialEvidenceFreshness.Stale) return null;
        var multiples = valuation.Metrics.Where(x => (x.Metric is "P/E" or "P/B" or "EV/EBITDA") && x.Value > 0).Select(x => x.Value).ToArray();
        return multiples.Length == 0 ? null : multiples.Average();
    }

    private static IEnumerable<ContributionScenario> GenerateSameClassSplitScenarios(
        decimal amount,
        IReadOnlyList<ContributionCandidate> candidates,
        ContributionCandidate dominant,
        IReadOnlyList<ContributionCandidate> eligible)
    {
        var alternatives = eligible
            .Where(x => x.CandidateKey != dominant.CandidateKey)
            .OrderByDescending(x => x.DecisionScore)
            .ThenBy(x => x.Ticker, StringComparer.Ordinal)
            .Take(3)
            .ToArray();

        foreach (var alternative in alternatives)
        foreach (var dominantPercentage in new[] { 30m, 40m, 50m })
        {
            var dominantAmount = Money(amount * dominantPercentage / 100m);
            var alternativeAmount = Money(amount - dominantAmount);
            if (dominantAmount <= 0 || alternativeAmount <= 0
                || dominantAmount > dominant.ClassBudget
                || alternativeAmount > alternative.ClassBudget)
                continue;

            yield return Scenario(candidates,
                new[]
                {
                    new CandidateAllocation(dominant.Identity, dominantAmount),
                    new CandidateAllocation(alternative.Identity, alternativeAmount)
                },
                $"Divide o aporte entre {dominant.Ticker} e {alternative.Ticker} para corrigir a classe sem ampliar excessivamente a concentração.");
        }
    }

    private static ContributionCandidate? DominantHeldCandidate(IReadOnlyList<ContributionCandidate> candidates)
        => candidates.Where(x => x.HasPosition && x.CurrentValue > 0)
            .OrderByDescending(x => x.CurrentValue)
            .ThenBy(x => x.Ticker, StringComparer.Ordinal)
            .FirstOrDefault();

    private static IEnumerable<ContributionScenario> GenerateSplitScenarios(
        decimal amount,
        IReadOnlyList<ContributionCandidate> candidates,
        IReadOnlyList<PortfolioNeed> needs)
    {
        var classes = needs
            .Select(need => new
            {
                Need = need,
                Candidates = candidates.Where(candidate => candidate.AssetClass == need.AssetClass && !candidate.HasBlock)
                    .OrderByDescending(candidate => candidate.DecisionScore)
                    .ThenBy(candidate => candidate.Ticker, StringComparer.Ordinal)
                    .Take(4)
                    .ToArray()
            })
            .Where(x => x.Candidates.Length > 0)
            .ToArray();

        foreach (var left in classes)
        foreach (var right in classes.Where(x => string.CompareOrdinal(x.Need.AssetClass.ToString(), left.Need.AssetClass.ToString()) > 0))
        foreach (var leftCandidate in left.Candidates)
        foreach (var rightCandidate in right.Candidates)
        foreach (var leftPercentage in new[] { 10m, 20m, 30m, 40m, 50m, 60m, 70m, 80m, 90m })
        {
            var leftAmount = Money(amount * leftPercentage / 100m);
            var rightAmount = Money(amount - leftAmount);
            if (leftAmount <= 0 || rightAmount <= 0
                || leftAmount > leftCandidate.ClassBudget
                || rightAmount > rightCandidate.ClassBudget)
                continue;

            yield return Scenario(candidates,
                new[] { new CandidateAllocation(leftCandidate.Identity, leftAmount), new CandidateAllocation(rightCandidate.Identity, rightAmount) },
                $"Compara {leftPercentage:0}% em {left.Need.AssetClass} e {100m - leftPercentage:0}% em {right.Need.AssetClass} pela melhora projetada da carteira.");
        }
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public PortfolioDecision Decide(decimal amount, IReadOnlyList<ContributionCandidate> candidates, IReadOnlyList<(Guid AssetId, decimal Amount)> recommended)
        => Decide(amount, candidates, recommended.Select(x => new CandidateAllocation(CandidateIdentity.Known(x.AssetId), x.Amount)).ToArray());

    public PortfolioDecision Decide(decimal amount, IReadOnlyList<ContributionCandidate> candidates, IReadOnlyList<CandidateAllocation> recommended)
    {
        candidates ??= Array.Empty<ContributionCandidate>();
        var exposure = PortfolioExposureAggregator.Aggregate(candidates);
        var secondary = PortfolioExposureAggregator.DetectNeeds(candidates, exposure);
        var structuralNeeds = PortfolioExposureAggregator.DetectStructuralNeeds(candidates, exposure);
        var missing = MissingData(exposure);
        var financial = candidates.Where(x => x.Identity.AssetId is not null).Select(x => CandidateFinancialEvaluator.Evaluate(x.Identity.AssetId!.Value, x.AssetType, x.FinancialSnapshot)).ToArray();
        var needs = candidates.GroupBy(x => x.AssetClass).Select(g => { var f = g.First(); return new PortfolioNeed(g.Key, f.CurrentClassWeight, f.TargetClassWeight, f.TargetClassWeight - f.CurrentClassWeight); }).Where(x => x.GapPercentagePoints > 2m).OrderByDescending(x => x.GapPercentagePoints).ThenBy(x => x.AssetClass.ToString(), StringComparer.Ordinal).ToArray();
        if (needs.Length == 0) return NoAction(amount, "Não há uma necessidade de alocação suficientemente relevante agora.", secondary, exposure, missing, financial) with { StructuralNeeds = structuralNeeds };
        var byKey = candidates.ToDictionary(x => x.CandidateKey);
        var allocations = recommended.Where(x => x.Amount > 0 && byKey.ContainsKey(x.Key)).ToArray();
        if (allocations.Length == 0) return NoAction(amount, "O Prumo não encontrou um candidato elegível para a necessidade atual.", secondary, exposure, missing, financial) with { StructuralNeeds = structuralNeeds };
        var scenario = Scenario(candidates, allocations, $"A classe {needs[0].AssetClass} está {needs[0].GapPercentagePoints:0.##} p.p. abaixo do target.");
        if (scenario.Improvement <= 0.01m) return NoAction(amount, "O Prumo não encontrou um aporte suficientemente adequado entre os candidatos conhecidos.", secondary, exposure, missing, financial) with { StructuralNeeds = structuralNeeds };
        var type = allocations.Length > 1 ? PortfolioDecisionType.SplitContribution : byKey[allocations[0].Key].HasPosition ? PortfolioDecisionType.ReinforceExisting : PortfolioDecisionType.AddNewAsset;
        var alternatives = candidates.Where(x => !x.HasBlock && x.AssetClass == needs[0].AssetClass && allocations.All(a => a.Key != x.CandidateKey)).OrderByDescending(x => FinancialRank(financial, x.CandidateKey)).ThenByDescending(x => x.DecisionScore).ThenBy(x => x.RecentContributionCount).ThenBy(x => x.Ticker, StringComparer.Ordinal).ThenBy(x => x.CandidateKey.Value, StringComparer.Ordinal).Take(2).Select(x => Scenario(candidates, new[] { new CandidateAllocation(x.Identity, amount) }, $"{x.Ticker} é uma alternativa conhecida para a classe atualmente mais defasada.")).Where(x => x.Improvement > 0.01m).ToArray();
        var safer = alternatives.FirstOrDefault(x => WorsensSevereConcentration(candidates, allocations) && !WorsensSevereConcentration(candidates, x.Allocations));
        if (safer is not null)
        {
            scenario = safer with { Reasons = scenario.Reasons.Concat(new[] { "A alternativa foi preferida porque não aumenta uma concentração severa de ativo." }).ToArray() };
            type = scenario.Allocations.Count > 1 ? PortfolioDecisionType.SplitContribution : byKey[scenario.Allocations[0].Key].HasPosition ? PortfolioDecisionType.ReinforceExisting : PortfolioDecisionType.AddNewAsset;
        }
        var winner = byKey[scenario.Allocations[0].Key];
        var concentration = WorsensSevereConcentration(candidates, scenario.Allocations) ? "Aumenta concentração severa." : "Não aumenta concentração severa.";
        var secondaryEffect = scenario.ExposureImpacts?.Any(x => x.AfterPercentage < x.BeforePercentage) == true ? "Melhora exposição secundária conhecida." : "Sem melhoria secundária comprovada.";
        var quality = financial.FirstOrDefault(x => x.AssetId == winner.Identity.AssetId)?.Quality.Category ?? "InsufficientData";
        var evidence = new IntegratedDecisionEvidence($"{needs[0].AssetClass} abaixo do target em {needs[0].GapPercentagePoints:0.##} p.p.", scenario.Improvement,
            WorsensSevereConcentration(candidates, scenario.Allocations) ? CandidateStructuralFit.AddressesPrimaryNeedButWorsensConcentration : CandidateStructuralFit.DirectlyAddressesPrimaryNeed,
            concentration, secondaryEffect, quality, "NotUsed",
            winner.RecentContributionCount > 0 ? "Recebeu aporte recentemente; contexto considerado." : "Sem aporte recente relevante.",
            safer is not null ? "LowerResultingConcentration" : "PrimaryNeedImprovement", alternatives.FirstOrDefault() is null ? null : "Alternativa estruturalmente comparável avaliada e não selecionada.");
        return new PortfolioDecision(type, amount, needs[0], scenario, alternatives, new[] { "O cenário recomendado produz a melhor melhora mensurável entre os candidatos avaliados." }, missing, secondary, exposure, financial, evidence) with { StructuralNeeds = structuralNeeds };
    }

    private static ContributionScenario Scenario(IReadOnlyList<ContributionCandidate> candidates, IReadOnlyList<CandidateAllocation> allocations, string reason)
    {
        var before = ContributionProjectionEvaluator.AggregateClassDeviation(candidates, Array.Empty<CandidateAllocation>());
        var after = ContributionProjectionEvaluator.AggregateClassDeviation(candidates, allocations);
        var beforeExposure = PortfolioExposureAggregator.Aggregate(candidates);
        var afterExposure = PortfolioExposureAggregator.Aggregate(candidates, allocations);
        var impacts = PortfolioExposureAggregator.Compare(beforeExposure, afterExposure);
        return new(allocations, before, after, before - after, new[] { reason }.Concat(impacts.Where(x => x.AfterPercentage < x.BeforePercentage).Take(2).Select(x => $"Reduz a concentração conhecida em {x.Dimension} ({x.Key}).")).ToArray(), afterExposure, impacts);
    }

    private static int FinancialRank(IReadOnlyList<CandidateFinancialEvaluation> values, CandidateKey key)
    {
        if (!Guid.TryParse(key.Value.Replace("KnownCatalog:", "", StringComparison.Ordinal), out var id)) return 0;
        var quality = values.FirstOrDefault(x => x.AssetId == id)?.Quality;
        return quality is { Freshness: FinancialEvidenceFreshness.Fresh, Category: "Acceptable" } ? 1 : quality is { Freshness: FinancialEvidenceFreshness.Fresh, Category: "Weak" } ? -1 : 0;
    }

    private static IReadOnlyList<string> MissingData(PortfolioExposureSummary x) => new[] { x.Sector, x.Geography, x.Currency }.Where(x => x.Coverage != ExposureCoverage.Complete).Select(x => $"{x.Dimension}: dados insuficientes.").ToArray();
    private static bool WorsensSevereConcentration(IReadOnlyList<ContributionCandidate> candidates, IReadOnlyList<CandidateAllocation> allocations)
    {
        var total = candidates.Sum(x => x.CurrentValue); var amount = allocations.Sum(x => x.Amount);
        if (total <= 0) return false;
        var before = candidates.Max(x => x.CurrentValue) / total * 100m;
        if (before < 20m) return false;
        var additions = allocations.GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.Sum(y => y.Amount));
        var after = candidates.Max(x => x.CurrentValue + additions.GetValueOrDefault(x.CandidateKey)) / (total + amount) * 100m;
        return after > before + 0.01m;
    }
    private static PortfolioDecision NoAction(decimal amount, string reason, IReadOnlyList<MultidimensionalPortfolioNeed> secondary, PortfolioExposureSummary exposure, IReadOnlyList<string> missing, IReadOnlyList<CandidateFinancialEvaluation> financial) => new(PortfolioDecisionType.NoAction, amount, null, null, Array.Empty<ContributionScenario>(), new[] { reason }, missing, secondary, exposure, financial);
}
