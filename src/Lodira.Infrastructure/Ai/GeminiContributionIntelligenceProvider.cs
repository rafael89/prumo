using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Lodira.Application.Contributions;
using Lodira.Application.Explainability;

namespace Lodira.Infrastructure.Ai;

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
        using var permit = await limiter.AcquireAsync(timeout.Token);
        var prompt = ContributionIntelligencePrompt.Build(portfolioId, amount, candidates);
        var request = new { contents = new[] { new { parts = new[] { new { text = prompt } } } }, generationConfig = new { responseMimeType = "application/json", responseJsonSchema = Schema } };
        var raw = await SendAsync(settings.Model, request, timeout.Token);
        return Parse(raw, settings.Model);
    }

    private async Task<string> SendAsync(string model, object body, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent") { Content = JsonContent.Create(body) };
        message.Headers.Add("x-goog-api-key", settings.ApiKey);
        using var response = await http.SendAsync(message, ct); var raw = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests && !string.IsNullOrWhiteSpace(settings.FallbackModel) && !string.Equals(model, settings.FallbackModel, StringComparison.OrdinalIgnoreCase)) return await SendAsync(settings.FallbackModel, body, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Gemini HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        return raw;
    }

    private static ContributionIntelligenceResult Parse(string raw, string model)
    {
        using var document = JsonDocument.Parse(raw); var root = document.RootElement; var text = root.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Resposta Gemini vazia.");
        var payload = JsonSerializer.Deserialize<Payload>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Resposta Gemini inválida.");
        var evaluations = payload.CandidateEvaluations?.Select(x => new ContributionCandidateEvaluation(x.AssetId, x.Rank, x.SuggestedAmount, x.Reason ?? string.Empty, x.Confidence, x.EvidenceReferences?.Select(e => new EvidenceReference(e.Type ?? "unknown", e.ReferenceId ?? string.Empty, e.Label ?? string.Empty, e.Summary)).ToArray() ?? Array.Empty<EvidenceReference>())).ToArray() ?? Array.Empty<ContributionCandidateEvaluation>();
        return new(evaluations, payload.OverallReasoning ?? string.Empty, Enum.TryParse<ContributionIntelligenceAvailability>(payload.IntelligenceAvailability, true, out var availability) ? availability : ContributionIntelligenceAvailability.PartialIntelligence, new("R4-8.0", "contribution", ContributionIntelligencePrompt.Version, "Gemini", model, DateTimeOffset.UtcNow));
    }

    private sealed record Payload(List<Evaluation>? CandidateEvaluations, string? OverallReasoning, string? IntelligenceAvailability);
    private sealed record Evaluation(Guid AssetId, int Rank, decimal SuggestedAmount, string? Reason, decimal Confidence, List<Evidence>? EvidenceReferences);
    private sealed record Evidence(string? Type, string? ReferenceId, string? Label, string? Summary);
    private static readonly object Schema = new { type = "object", properties = new { candidateEvaluations = new { type = "array", items = new { type = "object", properties = new { assetId = new { type = "string" }, rank = new { type = "integer" }, suggestedAmount = new { type = "number", minimum = 0 }, reason = new { type = "string" }, confidence = new { type = "number", minimum = 0, maximum = 1 }, evidenceReferences = new { type = "array", items = new { type = "object", properties = new { type = new { type = "string" }, referenceId = new { type = "string" }, label = new { type = "string" }, summary = new { type = "string" } }, required = new[] { "type", "referenceId", "label" } } } }, required = new[] { "assetId", "rank", "suggestedAmount", "reason", "confidence", "evidenceReferences" } } }, overallReasoning = new { type = "string" }, intelligenceAvailability = new { type = "string", @enum = new[] { "IntelligenceAvailable", "PartialIntelligence", "InsufficientIntelligence" } } }, required = new[] { "candidateEvaluations", "overallReasoning", "intelligenceAvailability" } };
}
