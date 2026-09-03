using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prumo.Application.Abstractions;
using Prumo.Application.Documents;

namespace Prumo.Infrastructure.Ai;

public sealed class GeminiAnalysisService : IAiAnalysisService
{
    private const string EndpointTemplate = "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";
    private readonly HttpClient httpClient;
    private readonly AiProviderOptions options;
    private readonly AiAnalysisValidator validator;
    private readonly ILogger<GeminiAnalysisService> logger;

    public GeminiAnalysisService(HttpClient httpClient, IOptions<AiProviderOptions> options, AiAnalysisValidator validator, ILogger<GeminiAnalysisService> logger)
    {
        this.httpClient = httpClient; this.options = options.Value; this.validator = validator; this.logger = logger;
        httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, this.options.TimeoutSeconds));
    }

    public string Model => options.Model;
    public IReadOnlyList<string> AcceptedModels => HasFallbackModel() ? new[] { options.Model, options.FallbackModel } : new[] { options.Model };
    public string PromptVersion => DocumentAnalysisPrompt.GlobalVersion;

    public async Task<AiAnalysisResult> AnalyzeAsync(string documentText, PortfolioAnalysisContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey)) throw new InvalidOperationException("AI:ApiKey não configurada para o provider Gemini.");
        if (string.IsNullOrWhiteSpace(options.Model)) throw new InvalidOperationException("AI:Model não configurado para o provider Gemini.");
        try
        {
            return await AnalyzeWithModelAsync(options.Model, documentText, context, cancellationToken);
        }
        catch (AiProviderRateLimitException) when (HasFallbackModel())
        {
            logger.LogWarning("Quota do modelo Gemini excedida; tentando fallback model={FallbackModel}; primaryModel={PrimaryModel}", options.FallbackModel, options.Model);
            return await AnalyzeWithModelAsync(options.FallbackModel, documentText, context, cancellationToken);
        }
        catch (HttpRequestException exception) when (HasFallbackModel() && IsTemporaryProviderFailure(exception))
        {
            logger.LogWarning(exception, "Modelo Gemini indisponível; tentando fallback model={FallbackModel}; primaryModel={PrimaryModel}", options.FallbackModel, options.Model);
            return await AnalyzeWithModelAsync(options.FallbackModel, documentText, context, cancellationToken);
        }
    }

    private bool HasFallbackModel() => !string.IsNullOrWhiteSpace(options.FallbackModel)
        && !string.Equals(options.Model, options.FallbackModel, StringComparison.OrdinalIgnoreCase);

    private static bool IsTemporaryProviderFailure(HttpRequestException exception)
        => exception.StatusCode is HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError;

    private async Task<AiAnalysisResult> AnalyzeWithModelAsync(string model, string documentText, PortfolioAnalysisContext context, CancellationToken cancellationToken)
    {
        var prompt = DocumentAnalysisPrompt.BuildGlobal(documentText, context);
        using var request = new HttpRequestMessage(HttpMethod.Post, string.Format(EndpointTemplate, Uri.EscapeDataString(model)))
        {
            Content = JsonContent.Create(new { contents = new[] { new { parts = new[] { new { text = prompt } } } }, generationConfig = new { responseMimeType = "application/json", responseJsonSchema = DocumentAnalysisJsonSchemas.CreateForGemini() } })
        };
        request.Headers.Add("x-goog-api-key", options.ApiKey);
        logger.LogInformation("Enviando análise ao provider {Provider}; model={Model}; textLength={TextLength}", "Gemini", options.Model, documentText.Length);
        var startedAt = Stopwatch.GetTimestamp();
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogInformation("Provider {Provider} respondeu HTTP {StatusCode}; durationMs={DurationMs}", "Gemini", (int)response.StatusCode, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        if (response.StatusCode == (HttpStatusCode)429) throw new AiProviderRateLimitException("Gemini limitou a requisição (HTTP 429).", response.StatusCode, ParseRetryAfter(response, rawResponse));
        if (!response.IsSuccessStatusCode) throw CreateHttpException(response.StatusCode, rawResponse);
        var envelope = ParseEnvelope(rawResponse);
        if (envelope.Usage is not null) logger.LogInformation("Uso do provider {Provider}; promptTokens={PromptTokens}; candidateTokens={CandidateTokens}; totalTokens={TotalTokens}", "Gemini", envelope.Usage.PromptTokens, envelope.Usage.CandidateTokens, envelope.Usage.TotalTokens);
        if (!string.IsNullOrWhiteSpace(envelope.ErrorMessage)) throw new InvalidOperationException($"Gemini retornou erro no envelope: {envelope.ErrorMessage}");
        if (!string.IsNullOrWhiteSpace(envelope.BlockReason)) throw new InvalidOperationException($"Gemini bloqueou a análise: {envelope.BlockReason}");
        if (!string.IsNullOrWhiteSpace(envelope.SafetyReason)) throw new InvalidOperationException($"Gemini bloqueou a análise por segurança: {envelope.SafetyReason}");
        if (string.IsNullOrWhiteSpace(envelope.Text)) throw new InvalidOperationException($"Resposta Gemini vazia ou sem conteúdo textual{(string.IsNullOrWhiteSpace(envelope.FinishReason) ? "." : $" (finishReason={envelope.FinishReason}).")}");
        var payload = JsonSerializer.Deserialize<AiAnalysisPayload>(envelope.Text, CreatePayloadJsonOptions()) ?? throw new InvalidOperationException("Resposta estruturada do Gemini vazia.");
        return validator.Validate(NormalizePayload(payload), rawResponse) with { Model = model };
    }

    private static AiAnalysisPayload NormalizePayload(AiAnalysisPayload payload) => payload with
    {
        Importance = NormalizeEnum(payload.Importance),
        Classification = NormalizeEnum(payload.Classification),
        SuggestedAction = NormalizeEnum(payload.SuggestedAction),
        Events = payload.Events?.Select(e => e with { EventType = NormalizeEnum(e.EventType) }).ToArray()
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
    private static JsonSerializerOptions CreatePayloadJsonOptions() { var o = new JsonSerializerOptions { PropertyNameCaseInsensitive = true }; o.Converters.Add(new LenientNullableDateTimeOffsetConverter()); return o; }
    private sealed class LenientNullableDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
    {
        public override bool HandleNull => true;
        public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType == JsonTokenType.Null ? null : reader.TokenType == JsonTokenType.String && DateTimeOffset.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value) ? value : null;
        public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options) { if (value.HasValue) writer.WriteStringValue(value.Value); else writer.WriteNullValue(); }
    }

    private static GeminiEnvelope ParseEnvelope(string rawResponse)
    {
        using var document = JsonDocument.Parse(rawResponse); var root = document.RootElement;
        var errorMessage = ReadString(root, "error", "message"); var blockReason = ReadString(root, "promptFeedback", "blockReason");
        var usage = root.TryGetProperty("usageMetadata", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object ? new GeminiUsage(GetInt(usageElement, "promptTokenCount"), GetInt(usageElement, "candidatesTokenCount"), GetInt(usageElement, "totalTokenCount")) : null;
        string? text = null, finishReason = null, safetyReason = null;
        if (root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array) foreach (var candidate in candidates.EnumerateArray())
        {
            finishReason ??= ReadString(candidate, "finishReason"); if (string.Equals(finishReason, "SAFETY", StringComparison.OrdinalIgnoreCase)) safetyReason = finishReason;
            if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array) foreach (var part in parts.EnumerateArray()) if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String) text ??= t.GetString();
        }
        return new GeminiEnvelope(text, finishReason, blockReason, safetyReason, errorMessage, usage);
    }
    private static string? ReadString(JsonElement e, string parent, string property) => e.TryGetProperty(parent, out var p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty(property, out var v) ? v.GetString() : null;
    private static string? ReadString(JsonElement e, string property) => e.TryGetProperty(property, out var v) ? v.GetString() : null;
    private static int? GetInt(JsonElement e, string property) => e.TryGetProperty(property, out var v) && v.TryGetInt32(out var result) ? result : null;
    private static Exception CreateHttpException(HttpStatusCode statusCode, string rawResponse) => statusCode switch
    {
        HttpStatusCode.BadRequest => new InvalidOperationException($"Gemini rejeitou a requisição (HTTP 400): {TrimError(rawResponse)}"),
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new InvalidOperationException($"Gemini rejeitou a autenticação/permissão (HTTP {(int)statusCode}). Verifique AI:ApiKey."),
        (HttpStatusCode)429 => new HttpRequestException("Gemini limitou a requisição (HTTP 429).", null, statusCode),
        HttpStatusCode.RequestTimeout => new HttpRequestException("Gemini excedeu o tempo limite (HTTP 408).", null, statusCode),
        _ when (int)statusCode >= 500 => new HttpRequestException($"Gemini indisponível (HTTP {(int)statusCode}).", null, statusCode),
        _ => new InvalidOperationException($"Gemini retornou HTTP {(int)statusCode}: {TrimError(rawResponse)}")
    };
    private static string TrimError(string value) => string.IsNullOrWhiteSpace(value) ? "resposta de erro vazia" : value.Length <= 500 ? value : value[..500];
    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response, string rawResponse)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero) return delta;
        if (response.Headers.RetryAfter?.Date is { } date && date - DateTimeOffset.UtcNow > TimeSpan.Zero) return date - DateTimeOffset.UtcNow;
        try
        {
            using var document = JsonDocument.Parse(rawResponse);
            if (document.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array) foreach (var detail in details.EnumerateArray()) if (detail.TryGetProperty("retryDelay", out var retry) && retry.ValueKind == JsonValueKind.String)
            {
                var value = retry.GetString()?.Trim(); if (value?.EndsWith("s", StringComparison.OrdinalIgnoreCase) == true && double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0) return TimeSpan.FromSeconds(seconds);
            }
        }
        catch (JsonException) { }
        return null;
    }
    private sealed record GeminiEnvelope(string? Text, string? FinishReason, string? BlockReason, string? SafetyReason, string? ErrorMessage, GeminiUsage? Usage);
    private sealed record GeminiUsage(int? PromptTokens, int? CandidateTokens, int? TotalTokens);

}
