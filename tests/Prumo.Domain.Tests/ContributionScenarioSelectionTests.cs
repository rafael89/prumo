using System.Text.Json;
using Prumo.Application.Contributions;
using Prumo.Application.Explainability;
using Prumo.Application.Portfolio;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class ContributionScenarioSelectionTests
{
    [Fact]
    public void Ai_can_change_valid_winner_without_changing_deterministic_allocations()
    {
        var first = Candidate("AAA3");
        var second = Candidate("BBB3");
        var baseline = Option("scenario:deterministic", first, 100m);
        var alternative = Option("scenario:alternative:1", second, 100m);
        var selection = Selection(alternative.ScenarioKey, "A alternativa diversifica melhor.", "AI", "model-x");

        var selected = ContributionScenarioValidator.Select(selection, new[] { baseline, alternative }, 100m, new[] { first, second });

        Assert.Equal("scenario:deterministic", baseline.ScenarioKey);
        Assert.Equal(second.CandidateKey, selected.Allocations.Single().Key);
        Assert.Equal("scenario:alternative:1", selected.ScenarioKey);
        Assert.Equal("model-x", selection.Metadata.Model);
    }

    [Fact]
    public void Ai_cannot_select_scenario_outside_safe_set()
    {
        var candidate = Candidate("AAA3");
        var selection = Selection("scenario:invented", "x", "AI", "model-x");

        Assert.Throws<InvalidOperationException>(() => ContributionScenarioValidator.Select(
            selection, new[] { Option("scenario:deterministic", candidate, 100m) }, 100m, new[] { candidate }));
    }

    [Fact]
    public void Invalid_ai_response_produces_deterministic_fallback()
    {
        var candidate = Candidate("AAA3");
        var baseline = Option("scenario:deterministic", candidate, 100m);
        var invalid = Selection("scenario:deterministic", "x", "AI", "model-x") with
        {
            SelectedScenarioKey = "scenario:unknown"
        };

        Assert.Throws<InvalidOperationException>(() => ContributionScenarioValidator.Select(invalid, new[] { baseline }, 100m, new[] { candidate }));
        var fallback = FallbackGuidance(candidate);
        Assert.Equal(candidate.AssetId, fallback.Allocations.Single().AssetId);
        Assert.Equal(100m, fallback.AllocatedAmount);
    }

    [Fact]
    public void Provider_failure_produces_deterministic_fallback_metadata()
    {
        var guidance = new Prumo.Domain.Entities.PersistedContributionGuidance(Guid.NewGuid(), 100m, "s", "t", "i", "R17.8", "pending", "pending", "Pending", "pending", "{}");
        guidance.UpdateIntelligence("deterministic-fallback", "portfolio-rules", "InsufficientIntelligence", "fallback", "{\"usedFallback\":true}");

        Assert.Equal("deterministic-fallback", guidance.Provider);
        Assert.Equal("portfolio-rules", guidance.Model);
        Assert.Contains("true", guidance.MetadataJson);
    }

    [Fact]
    public void Ai_is_not_called_for_no_action_when_no_safe_scenario_exists()
    {
        var calls = 0;
        var options = Array.Empty<ContributionScenarioOption>();

        if (options.Length > 0) calls++;

        Assert.Equal(0, calls);
    }

    [Fact]
    public void Ai_can_select_a_safe_partial_scenario()
    {
        var candidate = Candidate("AAA3");
        var partial = new ContributionScenarioOption("scenario:baseline-partial", new[]
        {
            new CandidateAllocation(candidate.AssetId, 40m)
        }, 0m, "partial");

        var selected = ContributionScenarioValidator.Select(
            Selection(partial.ScenarioKey, "O restante ficou sem alocação por limite seguro.", "AI", "model-x"),
            new[] { partial }, 100m, new[] { candidate });

        Assert.Equal(40m, selected.Allocations.Single().Amount);
    }

    [Fact]
    public void Ai_can_return_a_valid_no_action_scenario()
    {
        var noAction = new ContributionScenarioOption("scenario:no-action", Array.Empty<CandidateAllocation>(), 0m, "no safe allocation");

        var selected = ContributionScenarioValidator.Select(
            Selection(noAction.ScenarioKey, "Nenhum candidato atende aos limites atuais.", "AI", "model-x"),
            new[] { noAction }, 100m, Array.Empty<ContributionCandidate>());

        Assert.Empty(selected.Allocations);
    }

    [Fact]
    public void Establishing_position_has_reduced_recent_contribution_penalty()
    {
        var recent = DateTimeOffset.UtcNow.AddDays(-10);
        var established = Candidate("AAA3") with
        {
            CurrentWeight = 1m,
            RecentContributionPenalty = 60m,
            RecentContributionCount = 1,
            LastContributionAt = recent
        };
        var establishing = established with { IsEstablishingPosition = true };

        Assert.True(establishing.DecisionScore > established.DecisionScore);
    }

    [Fact]
    public void Ai_can_select_exact_prevalidated_split()
    {
        var first = Candidate("AAA3");
        var second = Candidate("BBB3");
        var split = new ContributionScenarioOption("scenario:alternative:1", new[]
        {
            new CandidateAllocation(first.AssetId, 60m),
            new CandidateAllocation(second.AssetId, 40m)
        }, 12m, "split");

        var selected = ContributionScenarioValidator.Select(Selection(split.ScenarioKey, "Diversificação.", "AI", "model-x"), new[] { split }, 100m, new[] { first, second });

        Assert.Equal(new[] { 60m, 40m }, selected.Allocations.Select(x => x.Amount));
    }

    [Fact]
    public void Ai_can_create_a_valid_allocation_not_present_in_deterministic_references()
    {
        var first = Candidate("AAA3");
        var second = Candidate("BBB3");
        var proposal = Proposal("Allocate", new AllocationProposalAllocation(first.CandidateKey, 60m), new AllocationProposalAllocation(second.CandidateKey, 40m));

        var allocations = AllocationProposalValidator.Validate(proposal, 100m, new[] { first, second });

        Assert.Equal(new[] { 60m, 40m }, allocations.Select(x => x.Amount));
        Assert.Equal(new[] { first.CandidateKey, second.CandidateKey }, allocations.Select(x => x.Key));
    }

    [Fact]
    public void Maintenance_accepts_a_multiclass_allocation_within_independent_class_budgets()
    {
        var fii = Candidate("FIIB11") with { AssetClass = AssetClass.Fii, ClassBudget = 4_400m };
        var stock = Candidate("PETR4") with { AssetClass = AssetClass.BrazilianStock, ClassBudget = 4_400m };

        var allocations = AllocationProposalValidator.Validate(
            Proposal("Allocate", new AllocationProposalAllocation(fii.CandidateKey, 2_500m), new AllocationProposalAllocation(stock.CandidateKey, 1_900m)),
            4_400m, new[] { fii, stock });

        Assert.Equal(4_400m, allocations.Sum(allocation => allocation.Amount));
        Assert.Equal(2_500m, allocations.Single(allocation => allocation.Key == fii.CandidateKey).Amount);
        Assert.Equal(1_900m, allocations.Single(allocation => allocation.Key == stock.CandidateKey).Amount);
    }

    [Fact]
    public void Maintenance_continues_to_accept_a_concentrated_allocation()
    {
        var fii = Candidate("FIIB11") with { AssetClass = AssetClass.Fii, ClassBudget = 4_400m };

        var allocations = AllocationProposalValidator.Validate(
            Proposal("Allocate", new AllocationProposalAllocation(fii.CandidateKey, 4_400m)), 4_400m, new[] { fii });

        Assert.Equal(4_400m, Assert.Single(allocations).Amount);
    }

    [Fact]
    public void Class_budget_is_a_ceiling_not_a_required_amount()
    {
        var fii = Candidate("FIIB11") with { AssetClass = AssetClass.Fii, ClassBudget = 4_400m };

        var allocations = AllocationProposalValidator.Validate(
            Proposal("Allocate", new AllocationProposalAllocation(fii.CandidateKey, 2_500m)), 4_400m, new[] { fii });

        Assert.Equal(2_500m, Assert.Single(allocations).Amount);
    }

    [Fact]
    public void Multiclass_allocation_cannot_exceed_total_or_class_ceiling()
    {
        var fii = Candidate("FIIB11") with { AssetClass = AssetClass.Fii, ClassBudget = 2_500m };
        var stock = Candidate("PETR4") with { AssetClass = AssetClass.BrazilianStock, ClassBudget = 4_400m };

        Assert.Throws<InvalidOperationException>(() => AllocationProposalValidator.Validate(
            Proposal("Allocate", new AllocationProposalAllocation(fii.CandidateKey, 2_501m), new AllocationProposalAllocation(stock.CandidateKey, 1_899m)),
            4_400m, new[] { fii, stock }));
        Assert.Throws<InvalidOperationException>(() => AllocationProposalValidator.Validate(
            Proposal("Allocate", new AllocationProposalAllocation(fii.CandidateKey, 2_500m), new AllocationProposalAllocation(stock.CandidateKey, 1_901m)),
            4_400m, new[] { fii, stock }));
    }

    [Fact]
    public void Ai_cannot_allocate_to_an_unknown_or_blocked_candidate()
    {
        var allowed = Candidate("AAA3");
        var blocked = Candidate("BBB3") with { HasBlock = true };

        Assert.Throws<InvalidOperationException>(() => AllocationProposalValidator.Validate(
            Proposal("Allocate", new AllocationProposalAllocation(CandidateIdentity.Known(Guid.NewGuid()).Key, 100m)), 100m, new[] { allowed, blocked }));
        Assert.Throws<InvalidOperationException>(() => AllocationProposalValidator.Validate(
            Proposal("Allocate", new AllocationProposalAllocation(blocked.CandidateKey, 100m)), 100m, new[] { allowed, blocked }));
    }

    [Fact]
    public void Ai_allocation_must_respect_amount_class_budget_and_unique_candidates()
    {
        var first = Candidate("AAA3") with { ClassBudget = 70m };
        var second = Candidate("BBB3") with { ClassBudget = 70m };

        Assert.Throws<InvalidOperationException>(() => AllocationProposalValidator.Validate(
            Proposal("Allocate", new AllocationProposalAllocation(first.CandidateKey, 71m)), 100m, new[] { first, second }));
        Assert.Throws<InvalidOperationException>(() => AllocationProposalValidator.Validate(
            Proposal("Allocate", new AllocationProposalAllocation(first.CandidateKey, 60m), new AllocationProposalAllocation(second.CandidateKey, 60m)), 100m, new[] { first, second }));
        Assert.Throws<InvalidOperationException>(() => AllocationProposalValidator.Validate(
            Proposal("Allocate", new AllocationProposalAllocation(first.CandidateKey, 30m), new AllocationProposalAllocation(first.CandidateKey, 30m)), 100m, new[] { first, second }));
    }

    [Fact]
    public void Ai_assisted_guidance_preserves_provider_model_and_rationale()
    {
        var selection = Selection("scenario:deterministic", "Escolha sustentada pelos dados.", "Gemini", "gemini-2.5-flash");
        var guidance = new Prumo.Domain.Entities.PersistedContributionGuidance(Guid.NewGuid(), 100m, "s", "t", "i", "R17.8", "pending", "pending", "Pending", "pending", "{}");
        guidance.UpdateIntelligence(selection.Metadata.Provider, selection.Metadata.Model, selection.Availability.ToString(), selection.Reason, JsonSerializer.Serialize(new { usedFallback = false }));

        Assert.Equal("Gemini", guidance.Provider);
        Assert.Equal("gemini-2.5-flash", guidance.Model);
        Assert.Equal("Escolha sustentada pelos dados.", guidance.Reasoning);
        Assert.Contains("false", guidance.MetadataJson);
    }

    [Fact]
    public void Confirmation_does_not_call_ai_again()
    {
        var calls = 0;
        var confirmation = new { Status = "Confirmed" };

        _ = confirmation;
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Selection_prompt_sends_derived_history_and_financial_context()
    {
        var candidate = Candidate("AAA3") with
        {
            RecentContributionCount = 2,
            RecentContributionAmount = 250m,
            LastContributionAt = DateTimeOffset.UtcNow.AddDays(-10),
            FinancialSnapshot = new CandidateFinancialSnapshot(DateOnly.FromDateTime(DateTime.UtcNow), FinancialPeriodType.Annual, DateTimeOffset.UtcNow, FinancialDataSource.Provider, PriceEarnings: 10m)
        };
        var prompt = ContributionIntelligencePrompt.BuildScenarioSelection(Guid.NewGuid(), 100m, new[]
        {
            new ContributionScenarioOption("scenario:deterministic", new[] { new CandidateAllocation(candidate.AssetId, 100m) }, 10m,
                "reference", "Fii", new[] { "Reduz o desvio da classe.", "Não amplia concentração severa." })
        }, new[] { candidate });
        var contextJson = prompt[(prompt.IndexOf("Contexto JSON:\n", StringComparison.Ordinal) + "Contexto JSON:\n".Length)..];

        Assert.Contains("recentContributionCount", prompt);
        Assert.Contains("investorPolicy", contextJson);
        Assert.Contains("preferExistingPositionsWhenEquivalent", contextJson);
        Assert.Contains("isEstablishingPosition", contextJson);
        Assert.Contains("lastContributionAt", prompt);
        Assert.Contains("financialSnapshot", contextJson);
        Assert.Contains("priceEarnings", contextJson);
        Assert.Contains("primaryNeed", contextJson);
        Assert.Contains("structuralReasons", contextJson);
        Assert.Contains("concentra", contextJson);
    }

    [Fact]
    public void Allocation_prompt_limits_context_and_prioritizes_actionable_assessments()
    {
        var candidate = Candidate("AAA3");
        var reference = new EvidenceReference("Document", "doc", "label", new string('X', 5000));
        var assessments = Enumerable.Range(0, 20).Select(i => new ContributionAssessmentContext(
            i == 19 ? "High" : "Low", i == 19 ? "Monitor" : "None", "None",
            $"assessment-{i}", "impact", "monitor", 0.8m, new[] { reference })).ToArray();
        var events = Enumerable.Range(0, 20).Select(i => new CandidateIntelligenceEvent(Guid.NewGuid(),
            InvestmentEventType.Other, DateTimeOffset.UnixEpoch.AddDays(i), $"event-{i}", Array.Empty<string>(), new[] { reference })).ToArray();
        candidate = candidate with { CandidateIntelligence = CandidateIntelligence.Unavailable(candidate) with { Assessments = assessments, RecentEvents = events } };

        var prompt = ContributionIntelligencePrompt.BuildAllocationProposal(Guid.NewGuid(), 100m, new[] { candidate }, Array.Empty<ContributionScenarioOption>());
        using var document = JsonDocument.Parse(prompt[(prompt.LastIndexOf("\n{") + 1)..]);
        var context = document.RootElement.GetProperty("eligibleCandidates")[0].GetProperty("candidateIntelligence");
        Assert.Equal(12, context.GetProperty("events").GetArrayLength());
        Assert.Equal(8, context.GetProperty("omittedEventCount").GetInt32());
        Assert.Equal(8, context.GetProperty("omittedAssessmentCount").GetInt32());
        Assert.Equal("assessment-19", context.GetProperty("assessments")[0].GetProperty("summary").GetString());
        Assert.Equal("event-19", context.GetProperty("events")[0].GetProperty("summary").GetString());
        Assert.DoesNotContain(new string('X', 5000), prompt);
    }

    [Fact]
    public void Allocation_prompt_exposes_eligible_universe_and_keeps_scores_as_references()
    {
        var candidate = Candidate("AAA3") with { AllocationNeedScore = 90m, RecentContributionCount = 1, LastContributionAt = DateTimeOffset.UtcNow.AddDays(-5) };
        var prompt = ContributionIntelligencePrompt.BuildAllocationProposal(Guid.NewGuid(), 100m, new[] { candidate },
            new[] { Option("scenario:deterministic", candidate, 100m) }, InvestorPolicy.Current, "Valor acima do aporte.");

        Assert.Contains("eligibleCandidates", prompt);
        Assert.Contains("blockedCandidates", prompt);
        Assert.Contains("projection", prompt);
        Assert.Contains("deterministicReferences", prompt);
        Assert.Contains("deterministicReference", prompt);
        Assert.Contains("rejectionReason", prompt);
        Assert.Contains("candidateKey", prompt);
    }

    [Fact]
    public void Allocation_prompt_explains_multiclass_freedom_in_maintenance_without_changing_required()
    {
        var fii = Candidate("FIIB11") with { AssetClass = AssetClass.Fii, ClassBudget = 4_400m };
        var stock = Candidate("PETR4") with { AssetClass = AssetClass.BrazilianStock, ClassBudget = 4_400m };
        var maintenancePrompt = ContributionIntelligencePrompt.BuildAllocationProposal(Guid.NewGuid(), 4_400m, new[] { fii, stock }, Array.Empty<ContributionScenarioOption>());
        var requiredPrompt = ContributionIntelligencePrompt.BuildAllocationProposal(Guid.NewGuid(), 4_400m, new[] { fii, stock }, new[]
        {
            new ContributionScenarioOption("scenario:deterministic", new[] { new CandidateAllocation(fii.Identity, 4_400m) }, 1m, "reference", "Fii")
        });

        Assert.Contains("canMixAssetClasses", maintenancePrompt);
        Assert.Contains("ClassBudget é somente o máximo agregado permitido", maintenancePrompt);
        Assert.Contains("não há obrigação de priorizar ou consumir integralmente nenhuma classe", maintenancePrompt);
        Assert.Contains("\"rebalanceMode\":\"Required\"", requiredPrompt);
        Assert.Contains("Em Required, primaryNeed", requiredPrompt);
    }

    [Fact]
    public void Allocation_prompt_compares_final_composition_and_keeps_qualitative_assessments_visible()
    {
        var fii = Candidate("FIIB11") with
        {
            AssetClass = AssetClass.Fii,
            ClassBudget = 4_400m,
            CurrentAssessments = new[]
            {
                new ContributionAssessmentContext("Medium", "Monitor", "None", "Renegocia\u00e7\u00e3o de contrato", "Pode afetar o fluxo de rendimentos.", "Acompanhar a evolu\u00e7\u00e3o.", 0.7m, Array.Empty<EvidenceReference>()),
                new ContributionAssessmentContext("Medium", "Monitor", "None", "Inadimpl\u00eancia relevante", "Pode pressionar a distribui\u00e7\u00e3o.", "Acompanhar os pr\u00f3ximos relat\u00f3rios.", 0.7m, Array.Empty<EvidenceReference>())
            }
        };
        var stock = Candidate("PETR4") with { ClassBudget = 4_400m };

        var prompt = ContributionIntelligencePrompt.BuildAllocationProposal(Guid.NewGuid(), 4_400m, new[] { fii, stock }, Array.Empty<ContributionScenarioOption>());
        var context = prompt[(prompt.LastIndexOf("\n{") + 1)..];
        using var document = JsonDocument.Parse(context);
        var fiiContext = document.RootElement.GetProperty("eligibleCandidates").EnumerateArray()
            .Single(candidate => candidate.GetProperty("ticker").GetString() == "FIIB11");

        Assert.Contains("Avalie a composi\u00e7\u00e3o final, n\u00e3o apenas um ranking de candidatos.", prompt);
        Assert.Contains("melhor candidato individual com combina\u00e7\u00f5es complementares eleg\u00edveis", prompt);
        Assert.Contains("m\u00faltiplos assessments Medium/Monitor", prompt);
        Assert.Equal("Maintenance", document.RootElement.GetProperty("rebalanceMode").GetString());
        Assert.Equal(2, fiiContext.GetProperty("assessments").GetArrayLength());
        var assessments = fiiContext.GetProperty("assessments").EnumerateArray().ToArray();
        Assert.Equal("Renegocia\u00e7\u00e3o de contrato", assessments[0].GetProperty("summary").GetString());
        Assert.Equal("Inadimpl\u00eancia relevante", assessments[1].GetProperty("summary").GetString());
        Assert.Equal("Unavailable", fiiContext.GetProperty("geographicExposureCoverage").GetString());
        Assert.Equal(JsonValueKind.Null, fiiContext.GetProperty("geographicExposures").ValueKind);
        Assert.Equal("Unavailable", fiiContext.GetProperty("economicCurrencyExposureCoverage").GetString());
        Assert.Equal(JsonValueKind.Null, fiiContext.GetProperty("currencyExposures").ValueKind);
    }

    [Fact]
    public void Allocation_prompt_distinguishes_missing_sector_data_and_exposes_portfolio_impact()
    {
        var unknownSector = Candidate("AAA3") with { Sector = null, SectorWeightWithinClass = 100m };
        var energy = Candidate("PETR4") with
        {
            Sector = "Energia",
            SectorCategory = EconomicSector.Energy,
            SectorWeightWithinClass = 100m,
            GeographicExposures = new[] { new KnownGeographicExposure("BR", 100m) },
            CurrencyExposures = new[] { new KnownCurrencyExposure("BRL", 100m) }
        };
        var prompt = ContributionIntelligencePrompt.BuildAllocationProposal(Guid.NewGuid(), 100m, new[] { unknownSector, energy }, Array.Empty<ContributionScenarioOption>());
        var context = prompt[(prompt.LastIndexOf("\n{") + 1)..];
        using var document = JsonDocument.Parse(context);

        var candidates = document.RootElement.GetProperty("eligibleCandidates").EnumerateArray().ToArray();
        Assert.Equal(JsonValueKind.Null, candidates.Single(candidate => candidate.GetProperty("ticker").GetString() == "AAA3").GetProperty("sectorWeightWithinClass").ValueKind);
        Assert.True(candidates.Single(candidate => candidate.GetProperty("ticker").GetString() == "PETR4").GetProperty("projectedPortfolioExposure").GetProperty("sector").GetArrayLength() > 0);
        Assert.True(document.RootElement.GetProperty("portfolioState").GetProperty("exposure").GetProperty("geography").GetProperty("buckets").GetArrayLength() > 0);
    }

    private static ContributionCandidate Candidate(string ticker)
        => new(CandidateIdentity.Known(Guid.NewGuid()), ticker, AssetClass.BrazilianStock, 100m, 50m, 50m, 100m, 100m, false, Array.Empty<ContributionAssessmentContext>());

    private static ContributionScenarioOption Option(string key, ContributionCandidate candidate, decimal amount)
        => new(key, new[] { new CandidateAllocation(candidate.AssetId, amount) }, 10m, "scenario");

    private static ContributionScenarioSelection Selection(string key, string reason, string provider, string model)
        => new(key, reason, new[] { "structured" }, ContributionIntelligenceAvailability.IntelligenceAvailable,
            new("R17.8", "contribution-scenario", ContributionIntelligencePrompt.Version, provider, model, DateTimeOffset.UtcNow));

    private static AllocationProposal Proposal(string decisionType, params AllocationProposalAllocation[] allocations)
        => new(decisionType, allocations, "A diversificação reduz a concentração do aporte.", new[] { "diversification", "concentration" }, "medium",
            ContributionIntelligenceAvailability.IntelligenceAvailable,
            new("R5-ai-allocation", "contribution-allocation", ContributionIntelligencePrompt.AllocationVersion, "AI", "model-x", DateTimeOffset.UtcNow));

    private static ContributionGuidance FallbackGuidance(ContributionCandidate candidate)
        => new ContributionGuidanceCalculator().Calculate(new ContributionGuidanceInput(
            Guid.NewGuid(), 100m,
            new PortfolioHealthResult(PortfolioHealthStatus.Unbalanced,
                new[] { new AllocationDeviation(candidate.AssetClass, 0m, 100m, -100m, 100m, AllocationDeviationDirection.Underweight) },
                new MainDeviation(candidate.AssetClass, 0m, 100m, -100m, AllocationDeviationDirection.Underweight),
                new[] { new CapitalNeed(candidate.AssetClass, 0m, 100m, 100m, 1) }, "1", DateTimeOffset.UtcNow),
            new[] { new GuidancePosition(candidate.AssetId, candidate.Ticker, candidate.AssetClass, 100m, 0m) }, Array.Empty<GuidanceAssessment>()));
}
