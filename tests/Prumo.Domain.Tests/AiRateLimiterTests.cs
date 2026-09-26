using Microsoft.Extensions.Options;
using Prumo.Infrastructure.Ai;

namespace Prumo.Domain.Tests;

public sealed class AiRateLimiterTests
{
    [Fact]
    public async Task Disabled_allows_parallel_permits_without_waiting()
    {
        using var limiter = new AiRequestRateLimiter(Options.Create(new AiProviderOptions
        {
            RateLimit = new AiRateLimitOptions { Enabled = false }
        }));

        using var first = await limiter.AcquireAsync(TestContext.Current.CancellationToken);
        using var second = await limiter.AcquireAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Enabled_requires_positive_rate()
    {
        Assert.Throws<InvalidOperationException>(() => new AiRequestRateLimiter(Options.Create(new AiProviderOptions
        {
            RateLimit = new AiRateLimitOptions { Enabled = true, MaxRequestsPerMinute = 0 }
        })));
    }

    [Fact]
    public async Task Rejects_request_larger_than_tpm()
    {
        using var limiter = new AiRequestRateLimiter(Options.Create(new AiProviderOptions
        {
            RateLimit = new AiRateLimitOptions { Enabled = true, MaxRequestsPerMinute = 60, MaxTokensPerMinute = 10 }
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => limiter.AcquireAsync(11, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rpd_cancels_when_daily_quota_is_exhausted()
    {
        using var limiter = new AiRequestRateLimiter(Options.Create(new AiProviderOptions
        {
            RateLimit = new AiRateLimitOptions { Enabled = true, MaxRequestsPerMinute = 60, MaxRequestsPerDay = 1 }
        }));
        using var first = await limiter.AcquireAsync(TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => limiter.AcquireAsync(cancellation.Token));
    }
}
