using Prumo.Application.Abstractions;
using Prumo.Application.Contributions;
using Prumo.Application.Explainability;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using System.Text.Json;

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

    [Fact]
    public void R5_projection_removes_duplicate_assessments_but_keeps_event_provenance()
    {
        var asset = new Asset("PROJ3", "Teste", AssetType.Stock, "Industrial");
        var assessment = new ContributionAssessmentContext("Medium", "Monitor", "Caution", "Canonical assessment", "Motivo", "Acompanhar", .7m,
            new[] { new EvidenceReference("Document", "doc-1", "Documento") });
        var investmentEvent = new InvestmentEvent(Guid.NewGuid(), InvestmentEventType.Other, DateTimeOffset.UtcNow, "Evento conciso", new[] { "Fato estruturado" });
        investmentEvent.AffectedAssets.Add(asset);
        investmentEvent.Evidence.Add(new InvestmentEventEvidence(investmentEvent.Id, investmentEvent.DocumentAnalysisId, Guid.NewGuid(), "Documento do evento"));
        var fullCandidate = Candidate(asset) with
        {
            CurrentAssessments = new[] { assessment },
            CandidateIntelligence = CandidateIntelligenceBuilder.Build(Candidate(asset), asset, new[] { investmentEvent }, new[] { assessment })
        };
        var projected = fullCandidate with
        {
            CurrentAssessments = Array.Empty<ContributionAssessmentContext>(),
            CandidateIntelligence = CandidateIntelligenceBuilder.ProjectForR5(fullCandidate.CandidateIntelligence!)
        };

        Assert.True(JsonSerializer.Serialize(projected).Length < JsonSerializer.Serialize(fullCandidate).Length);
        Assert.Empty(projected.CurrentAssessments);
        Assert.Single(projected.CandidateIntelligence!.Assessments);
        Assert.Single(projected.CandidateIntelligence.RecentEvents);
        Assert.Contains(projected.CandidateIntelligence.RecentEvents[0].Evidence, x => x.Type == "Document");

        var proposal = ContributionIntelligencePrompt.BuildAllocationProposal(Guid.NewGuid(), 100m, new[] { projected }, Array.Empty<ContributionScenarioOption>());
        var reviewProjection = CompositionProjectionEvaluator.Project(new[] { projected }, Array.Empty<CandidateAllocation>());
        var review = ContributionIntelligencePrompt.BuildAllocationReview(Guid.NewGuid(), 100m, new[] { projected }, Array.Empty<ContributionScenarioOption>(), InvestorPolicy.Current, reviewProjection);
        Assert.Equal(1, Count(proposal, "Canonical assessment"));
        Assert.Equal(1, Count(review, "Canonical assessment"));
    }

    private static int Count(string text, string value)
        => text.Split(value, StringSplitOptions.None).Length - 1;

    private static ContributionCandidate Candidate(Asset asset, CandidateFinancialSnapshot? snapshot = null) =>
        new(CandidateIdentity.Known(asset.Id), asset.Ticker, asset.AssetClass, 100, 10, 100, 100, 100, false, Array.Empty<ContributionAssessmentContext>(), FinancialSnapshot: snapshot);
}
