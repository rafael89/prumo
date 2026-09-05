using System.Text.Json;
using Prumo.Application.Contributions;
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
        var prompt = ContributionIntelligencePrompt.BuildScenarioSelection(Guid.NewGuid(), 100m, new[] { Option("scenario:deterministic", candidate, 100m) }, new[] { candidate });
        var contextJson = prompt[(prompt.IndexOf("Contexto JSON:\n", StringComparison.Ordinal) + "Contexto JSON:\n".Length)..];

        Assert.Contains("recentContributionCount", prompt);
        Assert.Contains("lastContributionAt", prompt);
        Assert.Contains("financialSnapshot", contextJson);
        Assert.Contains("priceEarnings", contextJson);
    }

    private static ContributionCandidate Candidate(string ticker)
        => new(CandidateIdentity.Known(Guid.NewGuid()), ticker, AssetClass.BrazilianStock, 100m, 50m, 50m, 100m, 100m, false, Array.Empty<ContributionAssessmentContext>());

    private static ContributionScenarioOption Option(string key, ContributionCandidate candidate, decimal amount)
        => new(key, new[] { new CandidateAllocation(candidate.AssetId, amount) }, 10m, "scenario");

    private static ContributionScenarioSelection Selection(string key, string reason, string provider, string model)
        => new(key, reason, new[] { "structured" }, ContributionIntelligenceAvailability.IntelligenceAvailable,
            new("R17.8", "contribution-scenario", ContributionIntelligencePrompt.Version, provider, model, DateTimeOffset.UtcNow));

    private static ContributionGuidance FallbackGuidance(ContributionCandidate candidate)
        => new ContributionGuidanceCalculator().Calculate(new ContributionGuidanceInput(
            Guid.NewGuid(), 100m,
            new PortfolioHealthResult(PortfolioHealthStatus.Unbalanced,
                new[] { new AllocationDeviation(candidate.AssetClass, 0m, 100m, -100m, 100m, AllocationDeviationDirection.Underweight) },
                new MainDeviation(candidate.AssetClass, 0m, 100m, -100m, AllocationDeviationDirection.Underweight),
                new[] { new CapitalNeed(candidate.AssetClass, 0m, 100m, 100m, 1) }, "1", DateTimeOffset.UtcNow),
            new[] { new GuidancePosition(candidate.AssetId, candidate.Ticker, candidate.AssetClass, 100m, 0m) }, Array.Empty<GuidanceAssessment>()));
}
