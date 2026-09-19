using Prumo.Application.Contributions;
using Prumo.Infrastructure.Ai;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Prumo.Domain.Tests;

public sealed class GeminiContributionIntelligenceProviderTests
{
    [Fact]
    public void AppliesConfiguredTimeoutToContributionRequests()
    {
        using var http = new HttpClient();
        var settings = Options.Create(new AiProviderOptions
        {
            ApiKey = "test-key",
            Model = "test-model",
            TimeoutSeconds = 17
        });

        _ = new GeminiContributionIntelligenceProvider(http, settings, new ImmediateRateLimiter());

        Assert.Equal(TimeSpan.FromSeconds(17), http.Timeout);
    }

    [Fact]
    public void EnforcesMinimumTimeoutOfOneSecond()
    {
        using var http = new HttpClient();
        var settings = Options.Create(new AiProviderOptions
        {
            ApiKey = "test-key",
            Model = "test-model",
            TimeoutSeconds = 0
        });

        _ = new GeminiContributionIntelligenceProvider(http, settings, new ImmediateRateLimiter());

        Assert.Equal(TimeSpan.FromSeconds(1), http.Timeout);
    }

    [Fact]
    public async Task TimesOutWhileWaitingForRateLimitPermit()
    {
        using var http = new HttpClient();
        var settings = Options.Create(new AiProviderOptions
        {
            ApiKey = "test-key",
            Model = "test-model",
            TimeoutSeconds = 1
        });
        var provider = new GeminiContributionIntelligenceProvider(http, settings, new BlockingRateLimiter());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.EvaluateAsync(Guid.NewGuid(), 100m, Array.Empty<ContributionCandidate>()));
    }

    [Fact]
    public async Task Proposal_uses_contribution_model_thinking_medium_and_records_fallback_model()
    {
        using var httpHandler = new SequenceHandler(
            new Response(HttpStatusCode.ServiceUnavailable, "{}"),
            new Response(HttpStatusCode.ServiceUnavailable, "{}"),
            new Response(HttpStatusCode.OK, GeminiResponse("{\"decisionType\":\"NoAction\",\"allocations\":[],\"reason\":\"ok\",\"primaryFactors\":[],\"confidence\":\"low\"}")));
        using var http = new HttpClient(httpHandler);
        var settings = Options.Create(new AiProviderOptions
        {
            ApiKey = "test-key",
            Model = "gemini-3.5-flash-lite",
            FallbackModel = "gemini-3.1-flash-lite"
        });
        var provider = new GeminiContributionIntelligenceProvider(http, settings, new ImmediateRateLimiter());

        var result = await provider.ProposeAllocationAsync(Guid.NewGuid(), 100m, Array.Empty<ContributionCandidate>(), Array.Empty<ContributionScenarioOption>());

        Assert.Equal("gemini-3.1-flash-lite", result.Metadata.Model);
        Assert.Equal("gemini-3.5-flash-lite", result.Metadata.ConfiguredModel);
        Assert.Equal("gemini-3.1-flash-lite", result.Metadata.FallbackModel);
        Assert.True(result.Metadata.UsedFallback);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent", httpHandler.Requests[0].RequestUri!.ToString());
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-lite:generateContent", httpHandler.Requests[2].RequestUri!.ToString());
        using var body = JsonDocument.Parse(httpHandler.Bodies[0]);
        Assert.Equal("medium", body.RootElement.GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
    }

    [Fact]
    public async Task Review_uses_contribution_model_and_thinking_medium()
    {
        using var httpHandler = new SequenceHandler(new Response(HttpStatusCode.OK, GeminiResponse("{\"decision\":\"Confirm\",\"allocations\":[],\"reason\":\"ok\",\"considerations\":[]}")));
        using var http = new HttpClient(httpHandler);
        var settings = Options.Create(new AiProviderOptions
        {
            ApiKey = "test-key",
            Model = "gemini-3.5-flash-lite",
            FallbackModel = "gemini-3.1-flash-lite"
        });
        var provider = new GeminiContributionIntelligenceProvider(http, settings, new ImmediateRateLimiter());

        await provider.ReviewAllocationAsync(Guid.NewGuid(), 100m, Array.Empty<ContributionCandidate>(), Array.Empty<ContributionScenarioOption>(), InvestorPolicy.Current, CompositionProjectionEvaluator.Project(Array.Empty<ContributionCandidate>(), Array.Empty<CandidateAllocation>()));

        Assert.Contains("gemini-3.5-flash-lite", httpHandler.Requests[0].RequestUri!.ToString());
        using var body = JsonDocument.Parse(httpHandler.Bodies[0]);
        Assert.Equal("medium", body.RootElement.GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
    }

    private static string GeminiResponse(string text) => $"{{\"candidates\":[{{\"content\":{{\"parts\":[{{\"text\":{JsonSerializer.Serialize(text)}}}]}}}}]}}";

    private sealed record Response(HttpStatusCode StatusCode, string Body);

    private sealed class SequenceHandler(params Response[] responses) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();
        private int index;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            var response = responses[Math.Min(index++, responses.Length - 1)];
            return new HttpResponseMessage(response.StatusCode) { Content = new StringContent(response.Body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class ImmediateRateLimiter : IAiRequestRateLimiter
    {
        public Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDisposable>(new Permit());

        private sealed class Permit : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class BlockingRateLimiter : IAiRequestRateLimiter
    {
        public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new Permit();
        }

        private sealed class Permit : IDisposable
        {
            public void Dispose() { }
        }
    }
}
