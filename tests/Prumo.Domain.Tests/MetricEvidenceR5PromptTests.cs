using Prumo.Application.Contributions;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class MetricEvidenceR5PromptTests
{
    [Fact]
    public void Proposal_uses_metric_evidence_without_legacy_financial_snapshot_duplication()
    {
        var asset = new Asset("R5M3", "Metric", AssetType.Stock, "Industrial");
        asset.FundamentalSnapshots.Add(new(asset.Id, new(2025, 12, 31), FinancialPeriodType.Annual, FinancialDataSource.Provider, "fund-source", DateTimeOffset.UtcNow, revenue: 10, ebitda: 3, roic: .2m, netMargin: .1m));
        var candidate = Candidate(asset, DocumentFinancialQualityResolver.Resolve(asset));
        candidate = candidate with { CandidateIntelligence = CandidateIntelligenceBuilder.Build(candidate, asset, Array.Empty<InvestmentEvent>(), Array.Empty<ContributionAssessmentContext>()) };

        var prompt = ContributionIntelligencePrompt.BuildAllocationProposal(Guid.NewGuid(), 100m, new[] { candidate }, Array.Empty<ContributionScenarioOption>());

        Assert.Contains("MetricEvidence", prompt);
        Assert.Contains("Revenue", prompt);
        Assert.Contains("Ebitda", prompt);
        Assert.Contains("Roic", prompt);
        Assert.Contains("NetMargin", prompt);
        Assert.DoesNotContain("financialSnapshot", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_uses_the_same_metric_evidence_context()
    {
        var asset = new Asset("R5M4", "Metric", AssetType.Stock, "Industrial");
        asset.ValuationSnapshots.Add(new(asset.Id, DateOnly.FromDateTime(DateTime.UtcNow), FinancialDataSource.Provider, "valuation-source", DateTimeOffset.UtcNow, priceEarnings: 8));
        var candidate = Candidate(asset, DocumentFinancialQualityResolver.Resolve(asset));
        candidate = candidate with { CandidateIntelligence = CandidateIntelligenceBuilder.Build(candidate, asset, Array.Empty<InvestmentEvent>(), Array.Empty<ContributionAssessmentContext>()) };
        var projection = CompositionProjectionEvaluator.Project(new[] { candidate }, Array.Empty<CandidateAllocation>());

        var prompt = ContributionIntelligencePrompt.BuildAllocationReview(Guid.NewGuid(), 100m, new[] { candidate }, Array.Empty<ContributionScenarioOption>(), InvestorPolicy.Current, projection);

        Assert.Contains("MetricEvidence", prompt);
        Assert.Contains("PriceEarnings", prompt);
        Assert.Contains("valuation-source", prompt);
        Assert.DoesNotContain("financialSnapshot", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Conflict_and_alternative_count_are_visible_without_application_resolution()
    {
        var asset = new Asset("R5M5", "Metric", AssetType.Stock, "Industrial");
        var first = new Document(asset.Id, DocumentType.QuarterlyReport, DateTimeOffset.UtcNow, "CVM", "source-a", "https://example.test/a", "fingerprint-a");
        var second = new Document(asset.Id, DocumentType.QuarterlyReport, DateTimeOffset.UtcNow, "CVM", "source-b", "https://example.test/b", "fingerprint-b");
        asset.DocumentFinancialFacts.Add(new(first.Id, asset.Id, FinancialFactMetric.Revenue, new(2025, 12, 31), FinancialPeriodType.Annual, 100, "BRL", "currency", "source-a", .9m));
        asset.DocumentFinancialFacts.Add(new(second.Id, asset.Id, FinancialFactMetric.Revenue, new(2025, 12, 31), FinancialPeriodType.Annual, 120, "BRL", "currency", "source-b", .9m));
        var candidate = Candidate(asset, DocumentFinancialQualityResolver.Resolve(asset));
        candidate = candidate with { CandidateIntelligence = CandidateIntelligenceBuilder.Build(candidate, asset, Array.Empty<InvestmentEvent>(), Array.Empty<ContributionAssessmentContext>()) };

        var prompt = ContributionIntelligencePrompt.BuildAllocationProposal(Guid.NewGuid(), 100m, new[] { candidate }, Array.Empty<ContributionScenarioOption>());

        Assert.Contains("\"conflict\":true", prompt);
        Assert.Contains("alternativeEvidenceCount", prompt);
        Assert.Contains("source-a", prompt);
        Assert.Contains("source-b", prompt);
    }

    [Fact]
    public void Fii_not_applicable_and_stale_states_are_explicit()
    {
        var fii = new Asset("R5F11", "FII", AssetType.Fii, "Brick");
        var old = new Asset("R5S3", "Stock", AssetType.Stock, "Industrial");
        old.FundamentalSnapshots.Add(new(old.Id, new(2020, 12, 31), FinancialPeriodType.Annual, FinancialDataSource.Provider, "old-source", DateTimeOffset.UtcNow.AddYears(-5), revenue: 10));
        var fiiCandidate = Candidate(fii, null) with { CandidateIntelligence = CandidateIntelligenceBuilder.Build(Candidate(fii), fii, Array.Empty<InvestmentEvent>(), Array.Empty<ContributionAssessmentContext>()) };
        var oldCandidate = Candidate(old, DocumentFinancialQualityResolver.Resolve(old));
        oldCandidate = oldCandidate with { CandidateIntelligence = CandidateIntelligenceBuilder.Build(oldCandidate, old, Array.Empty<InvestmentEvent>(), Array.Empty<ContributionAssessmentContext>()) };

        var prompt = ContributionIntelligencePrompt.BuildAllocationProposal(Guid.NewGuid(), 100m, new[] { fiiCandidate, oldCandidate }, Array.Empty<ContributionScenarioOption>());

        Assert.Contains("NotApplicable", prompt);
        Assert.Contains("Stale", prompt);
    }

    [Fact]
    public void All_eligible_candidates_remain_in_the_prompt()
    {
        var first = Candidate(new Asset("R5A3", "A", AssetType.Stock, "Industrial"));
        var second = Candidate(new Asset("R5B3", "B", AssetType.Stock, "Industrial"));

        var prompt = ContributionIntelligencePrompt.BuildAllocationProposal(Guid.NewGuid(), 100m, new[] { first, second }, Array.Empty<ContributionScenarioOption>());

        Assert.Contains(first.CandidateKey.Value, prompt);
        Assert.Contains(second.CandidateKey.Value, prompt);
    }

    [Fact]
    public void Legacy_snapshot_does_not_increase_the_metric_evidence_prompt_context()
    {
        var asset = new Asset("R5P3", "Payload", AssetType.Stock, "Industrial");
        asset.FundamentalSnapshots.Add(new(asset.Id, new(2025, 12, 31), FinancialPeriodType.Annual, FinancialDataSource.Provider, "payload-source", DateTimeOffset.UtcNow, revenue: 10));
        var legacy = DocumentFinancialQualityResolver.Resolve(asset);
        var withLegacy = Candidate(asset, legacy);
        withLegacy = withLegacy with { CandidateIntelligence = CandidateIntelligenceBuilder.Build(withLegacy, asset, Array.Empty<InvestmentEvent>(), Array.Empty<ContributionAssessmentContext>()) };
        var withoutLegacy = withLegacy with { FinancialSnapshot = null };

        var withLegacyPrompt = ContributionIntelligencePrompt.BuildAllocationProposal(Guid.NewGuid(), 100m, new[] { withLegacy }, Array.Empty<ContributionScenarioOption>());
        var withoutLegacyPrompt = ContributionIntelligencePrompt.BuildAllocationProposal(Guid.NewGuid(), 100m, new[] { withoutLegacy }, Array.Empty<ContributionScenarioOption>());

        Assert.Equal(withoutLegacyPrompt.Length, withLegacyPrompt.Length);
    }

    private static ContributionCandidate Candidate(Asset asset, CandidateFinancialSnapshot? snapshot = null)
        => new(CandidateIdentity.Known(asset.Id), asset.Ticker, asset.AssetClass, 100, 10, 100, 100, 100, false, Array.Empty<ContributionAssessmentContext>(), asset.AssetType, FinancialSnapshot: snapshot);
}
