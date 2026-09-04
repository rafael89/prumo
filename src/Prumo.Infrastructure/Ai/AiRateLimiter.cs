using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prumo.Application.Abstractions;

namespace Prumo.Infrastructure.Ai;

public interface IAiRequestRateLimiter
{
    Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default);

    Task<IDisposable> AcquireAsync(int estimatedTokens, CancellationToken cancellationToken = default)
        => AcquireAsync(cancellationToken);
}

public sealed class AiRequestRateLimiter : IAiRequestRateLimiter, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly long intervalTicks;
    private readonly bool enabled;
    private readonly int maxTokensPerMinute;
    private readonly int maxRequestsPerDay;
    private readonly Queue<(long Timestamp, int Tokens)> tokenReservations = new();
    private DateOnly requestDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private int requestsToday;
    private long nextPermitTimestamp;

    public AiRequestRateLimiter(IOptions<AiProviderOptions> options)
    {
        var rateLimit = options.Value.RateLimit;
        if (!rateLimit.Enabled) return;
        if (rateLimit.MaxRequestsPerMinute <= 0) throw new InvalidOperationException("AI:RateLimit:MaxRequestsPerMinute deve ser positivo quando o rate limit está habilitado.");
        if (rateLimit.MaxTokensPerMinute < 0 || rateLimit.MaxRequestsPerDay < 0) throw new InvalidOperationException("AI:RateLimit: TPM e RPD não podem ser negativos.");
        enabled = true;
        maxTokensPerMinute = rateLimit.MaxTokensPerMinute;
        maxRequestsPerDay = rateLimit.MaxRequestsPerDay;
        intervalTicks = (long)(Stopwatch.Frequency * (60d / rateLimit.MaxRequestsPerMinute));
    }

    public Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
        => AcquireAsync(0, cancellationToken);

    public async Task<IDisposable> AcquireAsync(int estimatedTokens, CancellationToken cancellationToken = default)
    {
        if (!enabled) return NoopPermit.Instance;
        if (estimatedTokens < 0) throw new ArgumentOutOfRangeException(nameof(estimatedTokens));
        if (maxTokensPerMinute > 0 && estimatedTokens > maxTokensPerMinute) throw new InvalidOperationException("A requisição Gemini excede o TPM configurado.");
        await gate.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                var now = Stopwatch.GetTimestamp();
                ResetDailyCounterIfNeeded();
                while (tokenReservations.TryPeek(out var reservation) && now - reservation.Timestamp >= Stopwatch.Frequency * 60) tokenReservations.Dequeue();

                var waitTicks = nextPermitTimestamp - now;
                if (maxRequestsPerDay > 0 && requestsToday >= maxRequestsPerDay)
                {
                    var untilReset = DateTime.UtcNow.Date.AddDays(1) - DateTime.UtcNow;
                    waitTicks = Math.Max(waitTicks, (long)(untilReset.TotalSeconds * Stopwatch.Frequency));
                }
                if (maxTokensPerMinute > 0 && tokenReservations.Sum(x => x.Tokens) + estimatedTokens > maxTokensPerMinute && tokenReservations.TryPeek(out var oldest))
                {
                    waitTicks = Math.Max(waitTicks, oldest.Timestamp + Stopwatch.Frequency * 60 - now);
                }
                if (waitTicks <= 0) break;
                await Task.Delay(TimeSpan.FromSeconds((double)waitTicks / Stopwatch.Frequency), cancellationToken);
            }

            var grantedAt = Stopwatch.GetTimestamp();
            requestsToday++;
            if (intervalTicks > 0) nextPermitTimestamp = grantedAt + intervalTicks;
            if (maxTokensPerMinute > 0 && estimatedTokens > 0) tokenReservations.Enqueue((grantedAt, estimatedTokens));
            return new Permit(gate);
        }
        catch
        {
            gate.Release();
            throw;
        }
    }

    public static int EstimateTokens(string text) => Math.Max(1, (text.Length + 3) / 4);

    private void ResetDailyCounterIfNeeded()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (today == requestDate) return;
        requestDate = today;
        requestsToday = 0;
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
        // ponytail: 2k tokens cover the structured prompt/context; use a provider tokenizer if TPM precision becomes critical.
        using var permit = await limiter.AcquireAsync(AiRequestRateLimiter.EstimateTokens(documentText) + 2000, cancellationToken);
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
