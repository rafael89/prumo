using Prumo.Application.Contributions;
using Prumo.Application.Explainability;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class ExternalCandidateLifecycleAcceptanceTests
{
    [Fact]
    public void External_candidate_can_win_through_the_same_applier()
    {
        var identity = CandidateIdentity.FromExternal(new("Brapi", "EXT11", "B3", AssetType.Stock));
        var candidate = Candidate(identity, "EXT11");
        var result = new ContributionIntelligenceResult(new[] { new ContributionCandidateEvaluation(identity.Key, 1, 100, "melhor", .9m, Array.Empty<EvidenceReference>()) }, "ext", ContributionIntelligenceAvailability.IntelligenceAvailable, Metadata());

        var guidance = ContributionIntelligenceApplier.Apply(new(ContributionGuidanceStatus.Calculated, Guid.NewGuid(), 100, Array.Empty<ContributionAllocation>(), Array.Empty<AvoidedCandidate>(), 100, ContributionAllocationStyle.NoAllocation, Array.Empty<string>(), Array.Empty<string>(), "p", Array.Empty<string>(), DateTimeOffset.UtcNow), result, new[] { candidate }, Array.Empty<GuidancePosition>());

        var allocation = Assert.Single(guidance.Allocations);
        Assert.Equal(identity.Key, allocation.Identity.Key);
        Assert.Null(allocation.AssetId);
    }

    [Fact]
    public void External_candidate_loses_without_being_materialized_by_decision()
    {
        var known = CandidateIdentity.Known(Guid.NewGuid());
        var external = CandidateIdentity.FromExternal(new("Brapi", "EXT11", "B3", AssetType.Stock));
        var candidates = new[] { Candidate(known, "KNOWN"), Candidate(external, "EXT11") };
        var decision = new PortfolioDecisionService().Decide(100, candidates, new[] { new CandidateAllocation(known, 100) });

        Assert.DoesNotContain(decision.RecommendedScenario!.Allocations, x => x.Identity.Origin == CandidateOrigin.ExternalDiscovery);
    }

    [Fact]
    public void Candidate_key_is_stable_across_reconstruction()
    {
        var external = new ExternalCandidateIdentity("Brapi", "EXT11", "B3", AssetType.Stock);
        Assert.Equal(CandidateIdentity.FromExternal(external).Key, CandidateIdentity.FromExternal(external).Key);
    }

    [Fact]
    public void Origin_alone_does_not_change_candidate_score()
    {
        var known = Candidate(CandidateIdentity.Known(Guid.NewGuid()), "AAA");
        var external = Candidate(CandidateIdentity.FromExternal(new("Brapi", "AAA", "B3", AssetType.Stock)), "AAA");
        Assert.Equal(known.DecisionScore, external.DecisionScore);
    }

    [Fact]
    public void External_identity_rejects_synthetic_asset_id()
    {
        Assert.Throws<ArgumentException>(() => new CandidateIdentity(CandidateOrigin.ExternalDiscovery, Guid.Empty, new ExternalCandidateIdentity("Brapi", "EXT11", "B3", AssetType.Stock)));
    }

    [Fact]
    public void Known_identity_rejects_missing_asset_id()
    {
        Assert.Throws<ArgumentException>(() => new CandidateIdentity(CandidateOrigin.KnownCatalog, null, null));
    }

    private static ContributionCandidate Candidate(CandidateIdentity identity, string ticker) => new(identity, ticker, AssetClass.BrazilianStock, 0, 0, 0, 100, 100, false, Array.Empty<ContributionAssessmentContext>(), AssetType.Stock, HasPosition: identity.Origin == CandidateOrigin.KnownCatalog, AllocationNeedScore: 100, UnderweightScore: 100);
    private static ContributionIntelligenceMetadata Metadata() => new("test", "test", "test", "test", "test", DateTimeOffset.UtcNow);
}
