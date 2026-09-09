using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Prumo.Application.Abstractions;
using Prumo.Application.Contributions;

namespace Prumo.Infrastructure.Ai;

public sealed class HttpContributionIntelligenceProvider : IContributionIntelligenceProvider
{
    private readonly HttpClient http;
    private readonly AiProviderOptions settings;
    private readonly IAiRequestRateLimiter limiter;

    public HttpContributionIntelligenceProvider(HttpClient http, IOptions<AiProviderOptions> options, IAiRequestRateLimiter limiter)
    {
        this.http = http;
        settings = options.Value;
        this.limiter = limiter;
        http.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds));
    }

    public Task<ContributionIntelligenceResult> EvaluateAsync(Guid portfolioId, decimal contributionAmount, IReadOnlyList<ContributionCandidate> candidates, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("O provider HTTP de contribuição usa seleção de cenários.");

    public async Task<ContributionScenarioSelection> SelectScenarioAsync(Guid portfolioId, decimal contributionAmount, IReadOnlyList<ContributionScenarioOption> scenarios, IReadOnlyList<ContributionCandidate> candidates, InvestorPolicy? investorPolicy = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.Endpoint)) throw new InvalidOperationException("Provider HTTP de IA não configurado.");
        if (string.IsNullOrWhiteSpace(settings.Model)) throw new InvalidOperationException("Modelo de IA não configurado.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds)));
        using var permit = await limiter.AcquireAsync(timeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint)
        {
            Content = JsonContent.Create(new Request(settings.Model, ContributionIntelligencePrompt.BuildScenarioSelection(portfolioId, contributionAmount, scenarios, candidates, investorPolicy ?? InvestorPolicy.Current)))
        };
        if (!string.IsNullOrWhiteSpace(settings.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        using var response = await http.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<SelectionPayload>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, timeout.Token)
            ?? throw new InvalidOperationException("Provider HTTP retornou JSON vazio.");
        if (string.IsNullOrWhiteSpace(payload.SelectedScenarioKey)) throw new InvalidOperationException("Provider HTTP retornou scenarioKey vazio.");
        return new(payload.SelectedScenarioKey, payload.Reason ?? string.Empty, payload.Considerations ?? Array.Empty<string>(), ContributionIntelligenceAvailability.IntelligenceAvailable,
            new("R17.8", "contribution-scenario", ContributionIntelligencePrompt.Version, "HTTP", settings.Model, DateTimeOffset.UtcNow));
    }

    public async Task<AllocationProposal> ProposeAllocationAsync(Guid portfolioId, decimal contributionAmount, IReadOnlyList<ContributionCandidate> candidates, IReadOnlyList<ContributionScenarioOption> deterministicReferences, InvestorPolicy? investorPolicy = null, string? rejectionReason = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.Endpoint)) throw new InvalidOperationException("Provider HTTP de IA não configurado.");
        if (string.IsNullOrWhiteSpace(settings.Model)) throw new InvalidOperationException("Modelo de IA não configurado.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds)));
        using var permit = await limiter.AcquireAsync(timeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint)
        {
            Content = JsonContent.Create(new Request(settings.Model, ContributionIntelligencePrompt.BuildAllocationProposal(portfolioId, contributionAmount, candidates, deterministicReferences, investorPolicy ?? InvestorPolicy.Current, rejectionReason)))
        };
        if (!string.IsNullOrWhiteSpace(settings.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        using var response = await http.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AllocationProposalPayload>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, timeout.Token)
            ?? throw new InvalidOperationException("Provider HTTP retornou JSON vazio.");
        return new(payload.DecisionType ?? string.Empty,
            payload.Allocations?.Select(x => new AllocationProposalAllocation(new CandidateKey(x.CandidateKey ?? string.Empty), x.Amount)).ToArray() ?? Array.Empty<AllocationProposalAllocation>(),
            payload.Reason ?? string.Empty, payload.PrimaryFactors ?? Array.Empty<string>(), payload.Confidence ?? "low",
            ContributionIntelligenceAvailability.IntelligenceAvailable,
            new("R5-ai-allocation-v2", "contribution-allocation", ContributionIntelligencePrompt.AllocationVersion, "HTTP", settings.Model, DateTimeOffset.UtcNow));
    }

    public async Task<AllocationReview> ReviewAllocationAsync(Guid portfolioId, decimal contributionAmount, IReadOnlyList<ContributionCandidate> candidates, IReadOnlyList<ContributionScenarioOption> deterministicReferences, InvestorPolicy investorPolicy, CompositionProjection projection, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.Endpoint)) throw new InvalidOperationException("Provider HTTP de IA não configurado.");
        if (string.IsNullOrWhiteSpace(settings.Model)) throw new InvalidOperationException("Modelo de IA não configurado.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds)));
        using var permit = await limiter.AcquireAsync(timeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint)
        {
            Content = JsonContent.Create(new Request(settings.Model, ContributionIntelligencePrompt.BuildAllocationReview(portfolioId, contributionAmount, candidates, deterministicReferences, investorPolicy, projection)))
        };
        if (!string.IsNullOrWhiteSpace(settings.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        using var response = await http.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AllocationReviewPayload>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, timeout.Token)
            ?? throw new InvalidOperationException("Provider HTTP retornou JSON vazio.");
        return new(payload.Decision ?? string.Empty,
            payload.Allocations?.Select(item => new AllocationProposalAllocation(new CandidateKey(item.CandidateKey ?? string.Empty), item.Amount)).ToArray() ?? Array.Empty<AllocationProposalAllocation>(),
            payload.Reason ?? string.Empty, payload.Considerations ?? Array.Empty<string>(), ContributionIntelligenceAvailability.IntelligenceAvailable,
            new("R5-ai-allocation-v2", "contribution-composition-review", ContributionIntelligencePrompt.AllocationReviewVersion, "HTTP", settings.Model, DateTimeOffset.UtcNow));
    }

    private sealed record Request(string Model, string Prompt);
    private sealed record SelectionPayload(string? SelectedScenarioKey, string? Reason, IReadOnlyList<string>? Considerations);
    private sealed record AllocationProposalPayload(string? DecisionType, IReadOnlyList<AllocationPayload>? Allocations, string? Reason, IReadOnlyList<string>? PrimaryFactors, string? Confidence);
    private sealed record AllocationReviewPayload(string? Decision, IReadOnlyList<AllocationPayload>? Allocations, string? Reason, IReadOnlyList<string>? Considerations);
    private sealed record AllocationPayload(string? CandidateKey, decimal Amount);
}
