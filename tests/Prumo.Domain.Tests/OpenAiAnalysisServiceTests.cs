using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Prumo.Application.Documents;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Ai;

namespace Prumo.Domain.Tests;

public sealed class OpenAiAnalysisServiceTests
{
    private static readonly PortfolioAnalysisContext Context = new("XPLG11", "Fii", "Logística", 10, 100, 1100, 25);

    [Fact]
    public async Task EnviaResponsesApiComStructuredOutputEEValidaPayload()
    {
        var handler = new StubHandler(_ => JsonResponse(ValidEnvelope()));
        var service = CreateService(handler);

        var result = await service.AnalyzeAsync("texto do documento", Context, TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisImportance.Informative, result.Importance);
        Assert.Equal(AnalysisClassification.Distribution, result.Classification);
        Assert.Equal(SuggestedAction.NoAction, result.SuggestedAction);
        Assert.Equal(new[] { "XPLG11" }, result.AffectedAssets);
        Assert.Contains("\"additionalProperties\":false", handler.RequestBody);
        Assert.Equal("Bearer test-key", handler.Authorization);
        Assert.Equal("https://api.openai.com/v1/responses", handler.RequestUri?.ToString());
    }

    [Fact]
    public async Task RejeitaNaoAutorizado()
    {
        var service = CreateService(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken));

        Assert.Contains("401", exception.Message);
    }

    [Fact]
    public async Task RejeitaRateLimitComoErroTemporario()
    {
        var service = CreateService(new StubHandler(_ => new HttpResponseMessage((HttpStatusCode)429)));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => service.AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
    }

    [Fact]
    public async Task RejeitaErroDoServidorComoErroTemporario()
    {
        var service = CreateService(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => service.AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
    }

    [Fact]
    public async Task PreservaCancelamentoDoChamador()
    {
        var service = CreateService(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ValidEnvelope())
        }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AnalyzeAsync("texto", Context, cancellation.Token));
    }

    [Fact]
    public async Task PropagaTimeoutDoHttpClientComoCancelamentoSemRetryInterno()
    {
        var service = CreateService(new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse(ValidEnvelope());
        }), timeoutSeconds: 1);

        await Assert.ThrowsAnyAsync<TaskCanceledException>(() => service.AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejeitaRespostaIncompleta()
    {
        var service = CreateService(new StubHandler(_ => JsonResponse("{\"status\":\"incomplete\",\"incomplete_details\":{\"reason\":\"max_output_tokens\"},\"output\":[]}")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken));

        Assert.Contains("incompleta", exception.Message);
    }

    [Fact]
    public async Task RejeitaRefusal()
    {
        var service = CreateService(new StubHandler(_ => JsonResponse("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"conteúdo não permitido\"}]}]}")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken));

        Assert.Contains("recusou", exception.Message);
    }

    [Fact]
    public async Task RejeitaEnvelopeSemOutputText()
    {
        var service = CreateService(new StubHandler(_ => JsonResponse("{\"status\":\"completed\",\"output\":[]}")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken));

        Assert.Contains("output_text", exception.Message);
    }

    [Fact]
    public async Task RejeitaJsonInternoInvalido()
    {
        var service = CreateService(new StubHandler(_ => JsonResponse("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"not-json\"}]}]}")));

        await Assert.ThrowsAsync<JsonException>(() => service.AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejeitaPayloadQueNaoPassaNoValidador()
    {
        var invalidPayload = "{\"importance\":\"RELEVANT\",\"classification\":\"OTHER\",\"requiresAction\":false,\"suggestedAction\":\"NO_ACTION\",\"summary\":\"\",\"impact\":\"impacto\",\"portfolioImpact\":\"impacto\",\"affectedAssets\":[],\"confidence\":1}";
        var service = CreateService(new StubHandler(_ => JsonResponse($"{{\"status\":\"completed\",\"output\":[{{\"type\":\"message\",\"content\":[{{\"type\":\"output_text\",\"text\":{JsonSerializer.Serialize(invalidPayload)}}}]}}]}}")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnalyzeAsync("texto", Context, TestContext.Current.CancellationToken));
    }

    private static OpenAiAnalysisService CreateService(HttpMessageHandler handler, int timeoutSeconds = 5) => new(
        new HttpClient(handler),
        Options.Create(new AiProviderOptions { ApiKey = "test-key", Model = "test-model", TimeoutSeconds = timeoutSeconds }),
        new AiAnalysisValidator(),
        NullLogger<OpenAiAnalysisService>.Instance);

    private static HttpResponseMessage JsonResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private static string ValidEnvelope() => """
        {
          "status": "completed",
          "usage": { "input_tokens": 10, "output_tokens": 20, "total_tokens": 30 },
          "output": [{
            "type": "message",
            "role": "assistant",
            "status": "completed",
            "content": [{
              "type": "output_text",
              "text": "{\"importance\":\"RELEVANT\",\"classification\":\"DISTRIBUTION\",\"requiresAction\":false,\"suggestedAction\":\"MONITOR\",\"summary\":\"Resumo\",\"impact\":\"Impacto\",\"portfolioImpact\":\"Impacto na carteira\",\"affectedAssets\":[\"XPLG11\"],\"confidence\":0.92}"
            }]
          }]
        }
        """;

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            this.responseFactory = (request, _) => Task.FromResult(responseFactory(request));
        }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        {
            this.responseFactory = responseFactory;
        }

        public string RequestBody { get; private set; } = string.Empty;
        public string? Authorization { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Authorization = request.Headers.Authorization?.ToString();
            RequestUri = request.RequestUri;
            cancellationToken.ThrowIfCancellationRequested();
            return await responseFactory(request, cancellationToken);
        }
    }
}
