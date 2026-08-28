using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Lodira.Application.Abstractions;
using Lodira.Application.Documents;

namespace Lodira.Infrastructure.Ai;

/// <summary>
/// Uses Lodira's custom HTTP contract: the request contains model and prompt,
/// and the response body is an AiAnalysisPayload. This is not a native
/// OpenAI, Gemini, Anthropic, or other commercial provider adapter.
/// </summary>
public sealed class HttpAiAnalysisService : IAiAnalysisService
{
    private readonly HttpClient httpClient;
    private readonly AiProviderOptions options;
    private readonly AiAnalysisValidator validator;

    public HttpAiAnalysisService(HttpClient httpClient, IOptions<AiProviderOptions> options, AiAnalysisValidator validator)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.validator = validator;
        httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.Value.TimeoutSeconds));
    }

    public string Model => options.Model;
    public string PromptVersion => DocumentAnalysisPrompt.GlobalVersion;

    public async Task<AiAnalysisResult> AnalyzeAsync(string documentText, PortfolioAnalysisContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.Endpoint)) throw new InvalidOperationException("Provider de IA não configurado.");
        if (string.IsNullOrWhiteSpace(options.Model)) throw new InvalidOperationException("Modelo de IA não configurado.");

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
        {
            Content = JsonContent.Create(new AiProviderRequest(options.Model, DocumentAnalysisPrompt.BuildGlobal(documentText, context)))
        };
        if (!string.IsNullOrWhiteSpace(options.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);
        var payload = JsonSerializer.Deserialize<AiAnalysisPayload>(rawResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (payload is null) throw new InvalidOperationException("Provider retornou JSON vazio.");

        return validator.Validate(payload, rawResponse);
    }

    private sealed record AiProviderRequest(string Model, string Prompt);
}
