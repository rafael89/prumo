using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prumo.Application.Abstractions;

namespace Prumo.Infrastructure.Ai;

public interface IAiRequestRateLimiter
{
    Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default);
}

public sealed class AiRequestRateLimiter : IAiRequestRateLimiter, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly long intervalTicks;
    private readonly bool enabled;
    private long nextPermitTimestamp;

    public AiRequestRateLimiter(IOptions<AiProviderOptions> options)
    {
        var rateLimit = options.Value.RateLimit;
        if (!rateLimit.Enabled) return;
        if (rateLimit.MaxRequestsPerMinute <= 0) throw new InvalidOperationException("AI:RateLimit:MaxRequestsPerMinute deve ser positivo quando o rate limit está habilitado.");
        enabled = true;
        intervalTicks = (long)(Stopwatch.Frequency * (60d / rateLimit.MaxRequestsPerMinute));
    }

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        if (!enabled) return NoopPermit.Instance;
        await gate.WaitAsync(cancellationToken);
        try
        {
            var waitTicks = nextPermitTimestamp - Stopwatch.GetTimestamp();
            if (waitTicks > 0) await Task.Delay(TimeSpan.FromSeconds((double)waitTicks / Stopwatch.Frequency), cancellationToken);
            if (intervalTicks > 0) nextPermitTimestamp = Stopwatch.GetTimestamp() + intervalTicks;
            return new Permit(gate);
        }
        catch
        {
            gate.Release();
            throw;
        }
    }

    public void Dispose() => gate.Dispose();

    private sealed class Permit(SemaphoreSlim gate) : IDisposable
    {
        private int released;
        public void Dispose() { if (Interlocked.Exchange(ref released, 1) == 0) gate.Release(); }
    }

    private sealed class NoopPermit : IDisposable
    {
        public static readonly NoopPermit Instance = new();
        public void Dispose() { }
    }
}

public sealed class RateLimitedAiAnalysisService : IAiAnalysisService
{
    private readonly IAiAnalysisService inner;
    private readonly IAiRequestRateLimiter limiter;
    private readonly ILogger<RateLimitedAiAnalysisService> logger;

    public RateLimitedAiAnalysisService(IAiAnalysisService inner, IAiRequestRateLimiter limiter, ILogger<RateLimitedAiAnalysisService> logger)
    { this.inner = inner; this.limiter = limiter; this.logger = logger; }

    public string Model => inner.Model;
    public IReadOnlyList<string> AcceptedModels => inner.AcceptedModels;
    public string PromptVersion => inner.PromptVersion;

    public async Task<AiAnalysisResult> AnalyzeAsync(string documentText, PortfolioAnalysisContext context, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("AI request waiting for rate-limit permit; provider={Provider}; model={Model}", inner.GetType().Name, Model);
        using var permit = await limiter.AcquireAsync(cancellationToken);
        logger.LogInformation("AI request started; provider={Provider}; model={Model}; operation={Operation}", inner.GetType().Name, Model, "DocumentAnalysis");
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await inner.AnalyzeAsync(documentText, context, cancellationToken);
            logger.LogInformation("AI request completed; provider={Provider}; model={Model}; operation={Operation}; durationMs={DurationMs}", inner.GetType().Name, Model, "DocumentAnalysis", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }
        catch (AiProviderRateLimitException exception)
        {
            logger.LogWarning("AI provider rate limited request; provider={Provider}; model={Model}; retryAfterSeconds={RetryAfterSeconds}", inner.GetType().Name, Model, exception.RetryAfter?.TotalSeconds);
            throw;
        }
    }
}

public sealed class AiProviderRateLimitException : HttpRequestException
{
    public AiProviderRateLimitException(string message, HttpStatusCode statusCode, TimeSpan? retryAfter)
        : base(message, null, statusCode) => RetryAfter = retryAfter;
    public TimeSpan? RetryAfter { get; }
}
