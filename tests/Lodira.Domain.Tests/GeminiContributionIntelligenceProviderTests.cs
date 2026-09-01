using Lodira.Application.Contributions;
using Lodira.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace Lodira.Domain.Tests;

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
