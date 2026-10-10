using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Prumo.Application.Documents;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Ai;

namespace Prumo.Domain.Tests;

public sealed class GeminiAnalysisServiceTests
{
    private static readonly PortfolioAnalysisContext Context = new("XPLG11", "Fii", "Logística", 10, 100, 1100, 25);

    [Fact]
    public async Task EnviaGenerateContentComSchemaEExtraiResposta()
    {
        var handler = new StubHandler(_ => JsonResponse(ValidResponse()));
        var result = await CreateService(handler).AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisImportance.Informative, result.Importance);
        Assert.Equal(AnalysisClassification.Distribution, result.Classification);
        Assert.Equal(SuggestedAction.NoAction, result.SuggestedAction);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-lite:generateContent", handler.RequestUri?.ToString());
        Assert.Contains("\"responseMimeType\":\"application/json\"", handler.RequestBody);
        Assert.Contains("\"responseJsonSchema\"", handler.RequestBody);
        Assert.Contains("\"occurredAt\":{\"type\":[\"string\",\"null\"],\"format\":\"date-time\"}", handler.RequestBody);
        Assert.DoesNotContain("additionalProperties", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "XPLG11" }, result.AffectedAssets);
    }

    [Fact]
    public async Task NormalizaEventTypeEAcaoNoActionRetornadosEmScreamingSnakeCase()
    {
        var handler = new StubHandler(_ => JsonResponse("""
            {"candidates":[{"content":{"parts":[{"text":"{\"importance\":\"RELEVANT\",\"classification\":\"OTHER\",\"requiresAction\":false,\"suggestedAction\":\"NO_ACTION\",\"summary\":\"Resumo\",\"impact\":\"Impacto\",\"affectedAssets\":[],\"confidence\":0.9,\"events\":[{\"eventType\":\"FINANCIAL_RESULT\",\"occurredAt\":null,\"summary\":\"Resultado\",\"facts\":[],\"affectedAssets\":[]}] }"} ]}}]}
            """));

        var result = await CreateService(handler).AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken);

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

        var result = await CreateService(handler).AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken);

        Assert.Equal(DateTimeOffset.Parse("2026-08-27T00:00:00Z"), Assert.Single(result.Events!).OccurredAt);
    }

    [Fact]
    public async Task UsaChaveNoHeaderProprioDoGemini()
    {
        var handler = new StubHandler(_ => JsonResponse(ValidResponse()));
        await CreateService(handler).AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken);

        Assert.Equal("test-key", handler.GeminiApiKey);
        Assert.Null(handler.Authorization);
    }

    [Fact]
    public async Task LeRetryDelayDoErroEstruturadoDoGemini()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"error\":{\"details\":[{\"retryDelay\":\"7s\"}]}}", Encoding.UTF8, "application/json")
        });

        var exception = await Assert.ThrowsAsync<AiProviderRateLimitException>(() => CreateService(handler).AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromSeconds(7), exception.RetryAfter);
    }

    [Fact]
    public async Task UsaModeloFallbackQuandoModeloPrincipalExcedeQuota()
    {
        var requests = new List<Uri>();
        var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!);
            return requests.Count <= 3
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : JsonResponse(ValidResponse());
        });
        var service = new GeminiAnalysisService(
            new HttpClient(handler),
            Options.Create(new AiProviderOptions
            {
                ApiKey = "test-key",
                Model = "gemini-3.5-flash-lite",
                FallbackModel = "gemini-3.1-flash-lite",
                TimeoutSeconds = 5
            }),
            new AiAnalysisValidator(),
            NullLogger<GeminiAnalysisService>.Instance, new ImmediateTimeProvider());

        var result = await service.AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken);

        Assert.Equal("gemini-3.1-flash-lite", result.Model);
        Assert.Equal(4, requests.Count);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-lite:generateContent", requests[3].ToString());
    }

    [Fact]
    public async Task Retenta503NoMesmoModeloApos15Segundos()
    {
        var requests = 0;
        var models = new List<string>();
        var time = new ImmediateTimeProvider();
        var service = new GeminiAnalysisService(
            new HttpClient(new StubHandler(request =>
            {
                models.Add(request.RequestUri!.AbsolutePath);
                return ++requests == 1
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : JsonResponse(ValidResponse());
            })),
            Options.Create(new AiProviderOptions { ApiKey = "test-key", Model = "primary", FallbackModel = "fallback" }),
            new AiAnalysisValidator(),
            NullLogger<GeminiAnalysisService>.Instance, time);

        var result = await service.AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken);

        Assert.Equal("primary", result.Model);
        Assert.Equal(2, requests);
        Assert.All(models, model => Assert.Contains("/primary:", model));
        Assert.Equal(new[] { TimeSpan.FromSeconds(15) }, time.Delays);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task ErrosTemporariosUsamTresTentativasPorModelo(HttpStatusCode status)
    {
        var time = new ImmediateTimeProvider();
        var models = new List<string>();
        var service = new GeminiAnalysisService(
            new HttpClient(new StubHandler(request =>
            {
                models.Add(request.RequestUri!.AbsolutePath);
                return new HttpResponseMessage(status);
            })),
            Options.Create(new AiProviderOptions { ApiKey = "test-key", Model = "primary", FallbackModel = "fallback" }),
            new AiAnalysisValidator(),
            NullLogger<GeminiAnalysisService>.Instance, time);

        var exception = await Assert.ThrowsAnyAsync<HttpRequestException>(() => service.AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken));

        Assert.Equal(status, exception.StatusCode);
        Assert.Equal(6, models.Count);
        Assert.All(models.Take(3), model => Assert.Contains("/primary:", model));
        Assert.All(models.Skip(3), model => Assert.Contains("/fallback:", model));
        Assert.Equal(Enumerable.Repeat(TimeSpan.FromSeconds(15), 4), time.Delays);
    }

    [Fact]
    public async Task CancelamentoInterrompeEsperaDo503SemFallback()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var requests = 0;
        var service = new GeminiAnalysisService(
            new HttpClient(new StubHandler(_ =>
            {
                requests++;
                cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            })),
            Options.Create(new AiProviderOptions { ApiKey = "test-key", Model = "primary", FallbackModel = "fallback" }),
            new AiAnalysisValidator(),
            NullLogger<GeminiAnalysisService>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AnalyzeAsync("texto", Context, cancellation.Token));

        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task MantemFallbackParaOutrosErrosTemporarios()
    {
        var requests = 0;
        var service = new GeminiAnalysisService(
            new HttpClient(new StubHandler(_ => ++requests <= 3
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : JsonResponse(ValidResponse()))),
            Options.Create(new AiProviderOptions { ApiKey = "test-key", Model = "primary", FallbackModel = "fallback" }),
            new AiAnalysisValidator(),
            NullLogger<GeminiAnalysisService>.Instance, new ImmediateTimeProvider());

        var result = await service.AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken);

        Assert.Equal("fallback", result.Model);
        Assert.Equal(4, requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, typeof(InvalidOperationException))]
    [InlineData(HttpStatusCode.Forbidden, typeof(InvalidOperationException))]
    [InlineData(HttpStatusCode.TooManyRequests, typeof(AiProviderRateLimitException))]
    [InlineData(HttpStatusCode.InternalServerError, typeof(HttpRequestException))]
    public async Task TrataErrosHttp(HttpStatusCode statusCode, Type exceptionType)
    {
        var exception = await Record.ExceptionAsync(() => CreateService(new StubHandler(_ => new HttpResponseMessage(statusCode))).AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken));
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
        await Assert.ThrowsAnyAsync<TaskCanceledException>(() => service.AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("{\"candidates\":[]}", "vazia")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"SAFETY\",\"content\":{\"parts\":[]}}]}", "segurança")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[]}}]}", "vazia")]
    public async Task RejeitaRespostaSemTextoOuBloqueada(string response, string expected)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(new StubHandler(_ => JsonResponse(response))).AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken));
        Assert.Contains(expected, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejeitaJsonInternoInvalido()
    {
        const string response = "{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"not-json\"}]}}]}";
        await Assert.ThrowsAsync<JsonException>(() => CreateService(new StubHandler(_ => JsonResponse(response))).AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ValidaPayloadInterno()
    {
        const string invalidPayload = "{\"importance\":\"RELEVANT\",\"classification\":\"OTHER\",\"requiresAction\":false,\"suggestedAction\":\"NO_ACTION\",\"summary\":\"\",\"impact\":\"impacto\",\"portfolioImpact\":\"impacto\",\"affectedAssets\":[],\"confidence\":1}";
        var response = $"{{\"candidates\":[{{\"content\":{{\"parts\":[{{\"text\":{JsonSerializer.Serialize(invalidPayload)}}}]}}}}]}}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(new StubHandler(_ => JsonResponse(response))).AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken));
    }

    private static GeminiAnalysisService CreateService(HttpMessageHandler handler, int timeoutSeconds = 5) => new(
        new HttpClient(handler),
        Options.Create(new AiProviderOptions { ApiKey = "test-key", Model = "gemini-2.5-flash-lite", TimeoutSeconds = timeoutSeconds }),
        new AiAnalysisValidator(),
        NullLogger<GeminiAnalysisService>.Instance, new ImmediateTimeProvider());

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
