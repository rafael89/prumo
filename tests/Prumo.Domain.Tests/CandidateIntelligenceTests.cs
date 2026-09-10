using Prumo.Application.Abstractions;
using Prumo.Application.Contributions;
using Prumo.Application.Explainability;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class CandidateIntelligenceTests
{
    [Fact]
    public void Missing_data_is_explicitly_unavailable_and_never_positive()
    {
        var asset = new Asset("XYZ3", "Teste", AssetType.Stock, "Industrial");
        var candidate = Candidate(asset);

        var intelligence = CandidateIntelligenceBuilder.Build(candidate, asset, Array.Empty<InvestmentEvent>(), Array.Empty<ContributionAssessmentContext>());

        Assert.Equal(CandidateIntelligenceSectionStatus.Unavailable, intelligence.Fundamentals.Status);
        Assert.Equal(CandidateIntelligenceSectionStatus.Unavailable, intelligence.Valuation.Status);
        Assert.Equal(CandidateIntelligenceSectionStatus.Unavailable, intelligence.IncomeQuality.Status);
        Assert.Equal(CandidateIntelligenceCoverage.Low, intelligence.Coverage);
        Assert.Empty(intelligence.Evidence);
    }

    [Fact]
    public void Stale_financial_data_is_exposed_as_stale_and_valuation_remains_evidence()
    {
        var asset = new Asset("ABC3", "Teste", AssetType.Stock, "Industrial");
        var snapshot = new CandidateFinancialSnapshot(new(2020, 1, 1), FinancialPeriodType.Annual, DateTimeOffset.UtcNow.AddYears(-2), FinancialDataSource.Provider, PriceEarnings: 4, SourceReference: "valuation:abc");
        var intelligence = CandidateIntelligenceBuilder.Build(candidate: Candidate(asset, snapshot), asset, Array.Empty<InvestmentEvent>(), Array.Empty<ContributionAssessmentContext>());

        Assert.Equal(CandidateIntelligenceSectionStatus.Stale, intelligence.Valuation.Status);
        Assert.Contains(intelligence.Valuation.Evidence, x => x.ReferenceId == "valuation:abc");
        Assert.DoesNotContain(intelligence.Valuation.Evidence, x => x.Label.Contains("barato", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Event_and_assessment_evidence_keep_stable_references()
    {
        var asset = new Asset("EVT3", "Teste", AssetType.Stock, "Industrial");
        var documentId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();
        var investmentEvent = new InvestmentEvent(analysisId, InvestmentEventType.Other, DateTimeOffset.UtcNow.AddDays(-1), "Evento", new[] { "Fato" });
        investmentEvent.AffectedAssets.Add(asset);
        investmentEvent.Evidence.Add(new InvestmentEventEvidence(investmentEvent.Id, analysisId, documentId, "Documento fonte"));
        var assessment = new ContributionAssessmentContext("Medium", "Monitor", "Caution", "Resumo", "Motivo", "Acompanhar", .7m,
            new[] { new EvidenceReference("PortfolioAssessment", "assessment:1", "Assessment") });

        var intelligence = CandidateIntelligenceBuilder.Build(Candidate(asset), asset, new[] { investmentEvent }, new[] { assessment });

        Assert.Single(intelligence.RecentEvents);
        Assert.Contains(intelligence.Evidence, x => x.Type == "Document" && x.ReferenceId == documentId.ToString());
        Assert.Contains(intelligence.Evidence, x => x.ReferenceId == "assessment:1");
    }

    [Fact]
    public void R5_prompt_treats_candidate_intelligence_as_non_positive_evidence()
    {
        var asset = new Asset("R5A3", "Teste", AssetType.Stock, "Industrial");
        var candidate = Candidate(asset) with { CandidateIntelligence = CandidateIntelligence.Unavailable(Candidate(asset)) };

        var prompt = ContributionIntelligencePrompt.BuildAllocationProposal(Guid.NewGuid(), 100m, new[] { candidate }, Array.Empty<ContributionScenarioOption>());

        Assert.Contains("Candidate Intelligence", prompt);
        Assert.Contains("unavailable", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not produce a recommendation", prompt);
    }

    private static ContributionCandidate Candidate(Asset asset, CandidateFinancialSnapshot? snapshot = null) =>
        new(CandidateIdentity.Known(asset.Id), asset.Ticker, asset.AssetClass, 100, 10, 100, 100, 100, false, Array.Empty<ContributionAssessmentContext>(), FinancialSnapshot: snapshot);
}
