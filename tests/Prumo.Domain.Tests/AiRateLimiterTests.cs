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

        using var first = await limiter.AcquireAsync();
        using var second = await limiter.AcquireAsync();
    }

    [Fact]
    public void Enabled_requires_positive_rate()
    {
        Assert.Throws<InvalidOperationException>(() => new AiRequestRateLimiter(Options.Create(new AiProviderOptions
        {
            RateLimit = new AiRateLimitOptions { Enabled = true, MaxRequestsPerMinute = 0 }
        })));
    }
}
