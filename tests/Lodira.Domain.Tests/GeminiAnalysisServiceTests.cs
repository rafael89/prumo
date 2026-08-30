using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Lodira.Application.Abstractions;
using Lodira.Application.Documents;
using Lodira.Domain.Enums;
using Lodira.Infrastructure.Ai;

namespace Lodira.Domain.Tests;

public sealed class GeminiAnalysisServiceTests
{
    private static readonly PortfolioAnalysisContext Context = new("XPLG11", "Fii", "Logística", 10, 100, 1100, 25);

    [Fact]
    public async Task EnviaGenerateContentComSchemaEExtraiResposta()
    {
        var handler = new StubHandler(_ => JsonResponse(ValidResponse()));
        var result = await CreateService(handler).AnalyzeAsync("texto", Context);

        Assert.Equal(AnalysisImportance.Relevant, result.Importance);
        Assert.Equal(AnalysisClassification.Distribution, result.Classification);
        Assert.Equal(SuggestedAction.Monitor, result.SuggestedAction);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-lite:generateContent", handler.RequestUri?.ToString());
        Assert.Contains("\"responseMimeType\":\"application/json\"", handler.RequestBody);
        Assert.DoesNotContain("additionalProperties", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "XPLG11" }, result.AffectedAssets);
    }

    [Fact]
    public async Task NormalizaEventTypeEAcaoNoActionRetornadosEmScreamingSnakeCase()
    {
        var handler = new StubHandler(_ => JsonResponse("""
            {"candidates":[{"content":{"parts":[{"text":"{\"importance\":\"RELEVANT\",\"classification\":\"OTHER\",\"requiresAction\":false,\"suggestedAction\":\"NO_ACTION\",\"summary\":\"Resumo\",\"impact\":\"Impacto\",\"affectedAssets\":[],\"confidence\":0.9,\"events\":[{\"eventType\":\"FINANCIAL_RESULT\",\"occurredAt\":null,\"summary\":\"Resultado\",\"facts\":[],\"affectedAssets\":[]}] }"} ]}}]}
            """));

        var result = await CreateService(handler).AnalyzeAsync("texto", Context);

        Assert.Equal(SuggestedAction.NoAction, result.SuggestedAction);
        var eventResult = Assert.Single(result.Events!);
        Assert.Equal(InvestmentEventType.FinancialResult, eventResult.EventType);
    }

    [Fact]
    public async Task AceitaDataDeCalendarioSemHorarioNosEventos()
    {
        const string payload = "{\"importance\":\"RELEVANT\",\"classification\":\"OTHER\",\"requiresAction\":false,\"suggestedAction\":\"NO_ACTION\",\"summary\":\"Resumo\",\"impact\":\"Impacto\",\"affectedAssets\":[],\"confidence\":0.9,\"events\":[{\"eventType\":\"FINANCIAL_RESULT\",\"occurredAt\":\"2026-08-27\",\"summary\":\"Resultado\",\"facts\":[],\"affectedAssets\":[]}] }";
        var response = $"{{\"candidates\":[{{\"content\":{{\"parts\":[{{\"text\":{JsonSerializer.Serialize(payload)}}}]}}}}]}}";
        var handler = new StubHandler(_ => JsonResponse(response));

        var result = await CreateService(handler).AnalyzeAsync("texto", Context);

        Assert.Equal(DateTimeOffset.Parse("2026-08-27T00:00:00Z"), Assert.Single(result.Events!).OccurredAt);
    }

    [Fact]
    public async Task UsaChaveNoHeaderProprioDoGemini()
    {
        var handler = new StubHandler(_ => JsonResponse(ValidResponse()));
        await CreateService(handler).AnalyzeAsync("texto", Context);

        Assert.Equal("test-key", handler.GeminiApiKey);
        Assert.Null(handler.Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, typeof(InvalidOperationException))]
    [InlineData(HttpStatusCode.Forbidden, typeof(InvalidOperationException))]
    [InlineData(HttpStatusCode.TooManyRequests, typeof(AiProviderRateLimitException))]
    [InlineData(HttpStatusCode.InternalServerError, typeof(HttpRequestException))]
    public async Task TrataErrosHttp(HttpStatusCode statusCode, Type exceptionType)
    {
        var exception = await Record.ExceptionAsync(() => CreateService(new StubHandler(_ => new HttpResponseMessage(statusCode))).AnalyzeAsync("texto", Context));
        Assert.NotNull(exception);
        Assert.IsType(exceptionType, exception);
    }

    [Fact]
    public async Task PreservaCancelamento()
    {
        var service = CreateService(new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return JsonResponse(ValidResponse());
        }), 1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AnalyzeAsync("texto", Context, new CancellationToken(true)));
    }

    [Fact]
    public async Task PropagaTimeoutComoCancelamento()
    {
        var service = CreateService(new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return JsonResponse(ValidResponse());
        }), 1);
        await Assert.ThrowsAnyAsync<TaskCanceledException>(() => service.AnalyzeAsync("texto", Context));
    }

    [Theory]
    [InlineData("{\"candidates\":[]}", "vazia")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"SAFETY\",\"content\":{\"parts\":[]}}]}", "segurança")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[]}}]}", "vazia")]
    public async Task RejeitaRespostaSemTextoOuBloqueada(string response, string expected)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(new StubHandler(_ => JsonResponse(response))).AnalyzeAsync("texto", Context));
        Assert.Contains(expected, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejeitaJsonInternoInvalido()
    {
        const string response = "{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"not-json\"}]}}]}";
        await Assert.ThrowsAsync<JsonException>(() => CreateService(new StubHandler(_ => JsonResponse(response))).AnalyzeAsync("texto", Context));
    }

    [Fact]
    public async Task ValidaPayloadInterno()
    {
        const string invalidPayload = "{\"importance\":\"RELEVANT\",\"classification\":\"OTHER\",\"requiresAction\":false,\"suggestedAction\":\"NO_ACTION\",\"summary\":\"\",\"impact\":\"impacto\",\"portfolioImpact\":\"impacto\",\"affectedAssets\":[],\"confidence\":1}";
        var response = $"{{\"candidates\":[{{\"content\":{{\"parts\":[{{\"text\":{JsonSerializer.Serialize(invalidPayload)}}}]}}}}]}}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(new StubHandler(_ => JsonResponse(response))).AnalyzeAsync("texto", Context));
    }

    private static GeminiAnalysisService CreateService(HttpMessageHandler handler, int timeoutSeconds = 5) => new(
        new HttpClient(handler),
        Options.Create(new AiProviderOptions { ApiKey = "test-key", Model = "gemini-2.5-flash-lite", TimeoutSeconds = timeoutSeconds }),
        new AiAnalysisValidator(),
        NullLogger<GeminiAnalysisService>.Instance);

    private static HttpResponseMessage JsonResponse(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private static string ValidResponse() => """
        {
          "candidates": [{
            "content": { "parts": [{ "text": "{\"importance\":\"RELEVANT\",\"classification\":\"DISTRIBUTION\",\"requiresAction\":false,\"suggestedAction\":\"MONITOR\",\"summary\":\"Resumo\",\"impact\":\"Impacto\",\"portfolioImpact\":\"Impacto na carteira\",\"affectedAssets\":[\"XPLG11\"],\"confidence\":0.92}" }] },
            "finishReason": "STOP"
          }],
          "usageMetadata": { "promptTokenCount": 10, "candidatesTokenCount": 20, "totalTokenCount": 30 }
        }
        """;

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) => this.responseFactory = (request, _) => Task.FromResult(responseFactory(request));
        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) => this.responseFactory = responseFactory;
        public string RequestBody { get; private set; } = string.Empty;
        public string? Authorization { get; private set; }
        public string? GeminiApiKey { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Authorization = request.Headers.Authorization?.ToString();
            GeminiApiKey = request.Headers.TryGetValues("x-goog-api-key", out var values) ? values.Single() : null;
            RequestUri = request.RequestUri;
            cancellationToken.ThrowIfCancellationRequested();
            return await responseFactory(request, cancellationToken);
        }
    }
}
