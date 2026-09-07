using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Prumo.Application.Contributions;
using Prumo.Application.Abstractions;
using Prumo.Application.Explainability;

namespace Prumo.Infrastructure.Ai;

public sealed class GeminiContributionIntelligenceProvider : IContributionIntelligenceProvider
{
    private readonly HttpClient http;
    private readonly AiProviderOptions settings;
    private readonly IAiRequestRateLimiter limiter;

    public GeminiContributionIntelligenceProvider(HttpClient http, IOptions<AiProviderOptions> options, IAiRequestRateLimiter limiter)
    {
        this.http = http;
        settings = options.Value;
        this.limiter = limiter;
        http.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds));
    }

    public async Task<ContributionIntelligenceResult> EvaluateAsync(Guid portfolioId, decimal amount, IReadOnlyList<ContributionCandidate> candidates, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.Model)) throw new InvalidOperationException("Gemini não configurado para Contribution Intelligence.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds)));
        var prompt = ContributionIntelligencePrompt.Build(portfolioId, amount, candidates);
        var estimatedTokens = AiRequestRateLimiter.EstimateTokens(prompt) + 2000;
        var request = new { contents = new[] { new { parts = new[] { new { text = prompt } } } }, generationConfig = new { responseMimeType = "application/json", responseJsonSchema = Schema } };
        var raw = await SendAsync(settings.Model, request, estimatedTokens, timeout.Token);
        return Parse(raw, settings.Model);
    }

    public async Task<ContributionScenarioSelection> SelectScenarioAsync(Guid portfolioId, decimal contributionAmount, IReadOnlyList<ContributionScenarioOption> scenarios, IReadOnlyList<ContributionCandidate> candidates, InvestorPolicy? investorPolicy = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.Model)) throw new InvalidOperationException("Gemini não configurado para Contribution Intelligence.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds)));
        var prompt = ContributionIntelligencePrompt.BuildScenarioSelection(portfolioId, contributionAmount, scenarios, candidates, investorPolicy ?? InvestorPolicy.Current);
        var estimatedTokens = AiRequestRateLimiter.EstimateTokens(prompt) + 2000;
        var request = new { contents = new[] { new { parts = new[] { new { text = prompt } } } }, generationConfig = new { responseMimeType = "application/json", responseJsonSchema = ScenarioSchema } };
        var raw = await SendAsync(settings.Model, request, estimatedTokens, timeout.Token);
        using var document = JsonDocument.Parse(raw);
        var text = document.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Resposta Gemini vazia.");
        var payload = JsonSerializer.Deserialize<ScenarioPayload>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Resposta Gemini inválida.");
        if (string.IsNullOrWhiteSpace(payload.SelectedScenarioKey)) throw new InvalidOperationException("Resposta Gemini sem scenarioKey.");
        return new(payload.SelectedScenarioKey, payload.Reason ?? string.Empty, payload.Considerations?.ToArray() ?? Array.Empty<string>(), ContributionIntelligenceAvailability.IntelligenceAvailable, new("R17.8", "contribution-scenario", ContributionIntelligencePrompt.Version, "Gemini", settings.Model, DateTimeOffset.UtcNow));
    }

    private async Task<string> SendAsync(string model, object body, int estimatedTokens, CancellationToken ct)
    {
        HttpStatusCode lastStatus = HttpStatusCode.ServiceUnavailable;
        var lastRaw = string.Empty;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var permit = await limiter.AcquireAsync(estimatedTokens, ct);
            using var message = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent") { Content = JsonContent.Create(body) };
            message.Headers.Add("x-goog-api-key", settings.ApiKey);
            using var response = await http.SendAsync(message, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);
            lastStatus = response.StatusCode;
            lastRaw = raw;
            if (response.IsSuccessStatusCode) return raw;

            var transient = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
            if (!transient || attempt == 1) break;
            await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), ct);
        }

        if (IsTransient(lastStatus) && !string.IsNullOrWhiteSpace(settings.FallbackModel) && !string.Equals(model, settings.FallbackModel, StringComparison.OrdinalIgnoreCase))
            return await SendAsync(settings.FallbackModel, body, estimatedTokens, ct);
        if (lastStatus != HttpStatusCode.OK) throw new HttpRequestException($"Gemini HTTP {(int)lastStatus}: {TrimError(lastRaw)}", null, lastStatus);
        return lastRaw;
    }

    private static bool IsTransient(HttpStatusCode statusCode) => statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private static ContributionIntelligenceResult Parse(string raw, string model)
    {
        using var document = JsonDocument.Parse(raw); var root = document.RootElement; var text = root.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Resposta Gemini vazia.");
        var payload = JsonSerializer.Deserialize<Payload>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Resposta Gemini inválida.");
        var evaluations = payload.CandidateEvaluations?.Select(x => new ContributionCandidateEvaluation(
            string.IsNullOrWhiteSpace(x.CandidateKey) ? CandidateIdentity.Known(x.AssetId ?? throw new InvalidOperationException("Avaliação Gemini sem candidateKey.")).Key : new CandidateKey(x.CandidateKey),
            x.Rank, x.SuggestedAmount, x.Reason ?? string.Empty, x.Confidence, x.EvidenceReferences?.Select(e => new EvidenceReference(e.Type ?? "unknown", e.ReferenceId ?? string.Empty, e.Label ?? string.Empty, e.Summary)).ToArray() ?? Array.Empty<EvidenceReference>())).ToArray() ?? Array.Empty<ContributionCandidateEvaluation>();
        return new(evaluations, payload.OverallReasoning ?? string.Empty, Enum.TryParse<ContributionIntelligenceAvailability>(payload.IntelligenceAvailability, true, out var availability) ? availability : ContributionIntelligenceAvailability.PartialIntelligence, new("R4-9.0", "contribution", ContributionIntelligencePrompt.Version, "Gemini", model, DateTimeOffset.UtcNow));
    }

    private static string TrimError(string value) => string.IsNullOrWhiteSpace(value) ? "resposta vazia" : value.Length <= 500 ? value : value[..500];

    private sealed record Payload(List<Evaluation>? CandidateEvaluations, string? OverallReasoning, string? IntelligenceAvailability);
    private sealed record ScenarioPayload(string? SelectedScenarioKey, string? Reason, List<string>? Considerations);
    private static readonly object ScenarioSchema = new { type = "object", properties = new { selectedScenarioKey = new { type = "string" }, reason = new { type = "string" }, considerations = new { type = "array", items = new { type = "string" } } }, required = new[] { "selectedScenarioKey", "reason", "considerations" } };
    private sealed record Evaluation(string? CandidateKey, Guid? AssetId, int Rank, decimal SuggestedAmount, string? Reason, decimal Confidence, List<Evidence>? EvidenceReferences);
    private sealed record Evidence(string? Type, string? ReferenceId, string? Label, string? Summary);
    private static readonly object Schema = new { type = "object", properties = new { candidateEvaluations = new { type = "array", items = new { type = "object", properties = new { candidateKey = new { type = "string" }, rank = new { type = "integer" }, suggestedAmount = new { type = "number", minimum = 0 }, reason = new { type = "string" }, confidence = new { type = "number", minimum = 0, maximum = 1 }, evidenceReferences = new { type = "array", items = new { type = "object", properties = new { type = "string", referenceId = new { type = "string" }, label = new { type = "string" }, summary = new { type = "string" } }, required = new[] { "type", "referenceId", "label" } } } }, required = new[] { "candidateKey", "rank", "suggestedAmount", "reason", "confidence", "evidenceReferences" } } }, overallReasoning = new { type = "string" }, intelligenceAvailability = new { type = "string", @enum = new[] { "IntelligenceAvailable", "PartialIntelligence", "InsufficientIntelligence" } } }, required = new[] { "candidateEvaluations", "overallReasoning", "intelligenceAvailability" } };
}
