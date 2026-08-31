using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Lodira.Application.Abstractions;
using Lodira.Application.Documents;

namespace Lodira.Infrastructure.Ai;

public sealed class OpenAiAnalysisService : IAiAnalysisService
{
    private const string Endpoint = "https://api.openai.com/v1/responses";
    private const string SchemaName = "lodira_document_analysis";
    private readonly HttpClient httpClient;
    private readonly AiProviderOptions options;
    private readonly AiAnalysisValidator validator;
    private readonly ILogger<OpenAiAnalysisService> logger;

    public OpenAiAnalysisService(
        HttpClient httpClient,
        IOptions<AiProviderOptions> options,
        AiAnalysisValidator validator,
        ILogger<OpenAiAnalysisService> logger)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.validator = validator;
        this.logger = logger;
        httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, this.options.TimeoutSeconds));
    }

    public string Model => options.Model;
    public string PromptVersion => DocumentAnalysisPrompt.GlobalVersion;

    public async Task<AiAnalysisResult> AnalyzeAsync(
        string documentText,
        PortfolioAnalysisContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("AI:ApiKey não configurada para o provider OpenAI.");
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            throw new InvalidOperationException("AI:Model não configurado para o provider OpenAI.");
        }

        var prompt = DocumentAnalysisPrompt.BuildGlobal(documentText, context);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new
            {
                model = options.Model,
                input = prompt,
                store = false,
                text = new
                {
                    format = new
                    {
                        type = "json_schema",
                        name = SchemaName,
                        strict = true,
                        schema = AnalysisSchema
                    }
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        logger.LogInformation("Enviando análise ao provider {Provider}; model={Model}; textLength={TextLength}", "OpenAI", options.Model, documentText.Length);
        var startedAt = Stopwatch.GetTimestamp();
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);
        var elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        logger.LogInformation("Provider {Provider} respondeu HTTP {StatusCode}; durationMs={DurationMs}", "OpenAI", (int)response.StatusCode, elapsedMilliseconds);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateHttpException(response.StatusCode, rawResponse);
        }

        var envelope = ParseEnvelope(rawResponse);
        if (envelope.Usage is not null)
        {
            logger.LogInformation("Uso do provider {Provider}; inputTokens={InputTokens}; outputTokens={OutputTokens}; totalTokens={TotalTokens}", "OpenAI", envelope.Usage.InputTokens, envelope.Usage.OutputTokens, envelope.Usage.TotalTokens);
        }

        if (!string.IsNullOrWhiteSpace(envelope.ErrorMessage))
        {
            throw new InvalidOperationException($"OpenAI retornou erro no envelope: {envelope.ErrorMessage}");
        }

        if (!string.Equals(envelope.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            var reason = envelope.IncompleteDetails?.Reason ?? envelope.Status ?? "status ausente";
            throw new InvalidOperationException($"Resposta OpenAI incompleta: {reason}.");
        }

        if (!string.IsNullOrWhiteSpace(envelope.Refusal))
        {
            throw new InvalidOperationException($"OpenAI recusou a análise: {envelope.Refusal}");
        }

        if (string.IsNullOrWhiteSpace(envelope.StructuredText))
        {
            throw new InvalidOperationException("Resposta OpenAI não contém output_text estruturado.");
        }

        var payload = JsonSerializer.Deserialize<AiAnalysisPayload>(
            envelope.StructuredText,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (payload is null)
        {
            throw new InvalidOperationException("Resposta estruturada da OpenAI vazia.");
        }

        return validator.Validate(NormalizePayload(payload), rawResponse);
    }

    private static AiAnalysisPayload NormalizePayload(AiAnalysisPayload payload) => payload with
    {
        Importance = NormalizeEnum(payload.Importance),
        Classification = NormalizeEnum(payload.Classification),
        SuggestedAction = NormalizeEnum(payload.SuggestedAction)
    };

    private static string? NormalizeEnum(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "CRITICAL" => "Critical",
        "RELEVANT" => "Relevant",
        "INFORMATIVE" => "Informative",
        "NO_ACTION" => "NoAction",
        "CORPORATE_ACTION" => "CorporateAction",
        "FINANCIAL_RESULT" => "FinancialResult",
        "DISTRIBUTION" => "Distribution",
        "CAPITAL_RAISE" => "CapitalRaise",
        "ASSET_TRANSACTION" => "AssetTransaction",
        "GOVERNANCE" => "Governance",
        "OPERATIONAL" => "Operational",
        "LIQUIDATION" => "Liquidation",
        "OTHER" => "Other",
        "MAINTAIN" => "Maintain",
        "INCREASE_POSITION" => "IncreasePosition",
        "REDUCE_EXPOSURE" => "ReduceExposure",
        "EVALUATE_SALE" => "EvaluateSale",
        "MONITOR" => "Monitor",
        "OPEN_POSITION" => "OpenPosition",
        _ => value
    };

    private static OpenAiEnvelope ParseEnvelope(string rawResponse)
    {
        using var document = JsonDocument.Parse(rawResponse);
        var root = document.RootElement;
        var status = root.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
        var incompleteDetails = root.TryGetProperty("incomplete_details", out var incompleteElement) && incompleteElement.ValueKind == JsonValueKind.Object
            ? new IncompleteDetails(incompleteElement.TryGetProperty("reason", out var reason) ? reason.GetString() : null)
            : null;
        var usage = root.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object
            ? new OpenAiUsage(
                GetInt(usageElement, "input_tokens"),
                GetInt(usageElement, "output_tokens"),
                GetInt(usageElement, "total_tokens"))
            : null;
        var errorMessage = root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.Object && errorElement.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString()
            : null;

        string? structuredText = null;
        string? refusal = null;
        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in content.EnumerateArray())
                    {
                        var type = part.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                        if (string.Equals(type, "output_text", StringComparison.OrdinalIgnoreCase) && part.TryGetProperty("text", out var text)) structuredText = text.GetString();
                        if (string.Equals(type, "refusal", StringComparison.OrdinalIgnoreCase) && part.TryGetProperty("refusal", out var refusalElement)) refusal = refusalElement.GetString();
                    }
                }
            }
        }

        return new OpenAiEnvelope(status, incompleteDetails, usage, structuredText, refusal, errorMessage);
    }

    private static int? GetInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result) ? result : null;

    private static Exception CreateHttpException(HttpStatusCode statusCode, string rawResponse) => statusCode switch
    {
        HttpStatusCode.Unauthorized => new InvalidOperationException("OpenAI rejeitou a autenticação (HTTP 401). Verifique AI:ApiKey."),
        (HttpStatusCode)429 => new HttpRequestException("OpenAI limitou a requisição (HTTP 429).", null, statusCode),
        _ when (int)statusCode >= 500 => new HttpRequestException($"OpenAI indisponível (HTTP {(int)statusCode}).", null, statusCode),
        _ => new InvalidOperationException($"OpenAI retornou HTTP {(int)statusCode}: {TrimError(rawResponse)}")
    };

    private static string TrimError(string value) => string.IsNullOrWhiteSpace(value) ? "resposta de erro vazia" : value.Length <= 500 ? value : value[..500];

    private sealed record OpenAiEnvelope(string? Status, IncompleteDetails? IncompleteDetails, OpenAiUsage? Usage, string? StructuredText, string? Refusal, string? ErrorMessage);
    private sealed record IncompleteDetails(string? Reason);
    private sealed record OpenAiUsage(int? InputTokens, int? OutputTokens, int? TotalTokens);

    private static readonly object AnalysisSchema = new
    {
        type = "object",
        properties = new
        {
            importance = new { type = "string", @enum = new[] { "CRITICAL", "RELEVANT", "INFORMATIVE", "NO_ACTION" } },
            classification = new { type = "string", @enum = new[] { "CORPORATE_ACTION", "FINANCIAL_RESULT", "DISTRIBUTION", "CAPITAL_RAISE", "ASSET_TRANSACTION", "GOVERNANCE", "OPERATIONAL", "LIQUIDATION", "OTHER" } },
            requiresAction = new { type = "boolean" },
            suggestedAction = new { type = "string", @enum = new[] { "MAINTAIN", "INCREASE_POSITION", "REDUCE_EXPOSURE", "EVALUATE_SALE", "MONITOR", "NO_ACTION", "OPEN_POSITION" } },
            summary = new { type = "string" },
            impact = new { type = "string" },
            affectedAssets = new { type = "array", items = new { type = "string" } },
            confidence = new { type = "number", minimum = 0, maximum = 1 },
            events = new { type = "array", items = new { type = "object", properties = new { eventType = new { type = "string", @enum = new[] { "FINANCIAL_RESULT", "DISTRIBUTION", "CAPITAL_RAISE", "CORPORATE_ACTION", "ASSET_TRANSACTION", "GOVERNANCE", "OPERATIONAL", "LIQUIDATION", "OTHER" } }, occurredAt = new { type = new[] { "string", "null" } }, summary = new { type = "string" }, facts = new { type = "array", items = new { type = "string" } }, affectedAssets = new { type = "array", items = new { type = "string" } } }, required = new[] { "eventType", "occurredAt", "summary", "facts", "affectedAssets" }, additionalProperties = false } }
        },
        required = new[] { "importance", "classification", "requiresAction", "suggestedAction", "summary", "impact", "affectedAssets", "confidence", "events" },
        additionalProperties = false
    };
}
