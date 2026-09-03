namespace Prumo.Infrastructure.Ai;

public sealed class AiProviderOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string FallbackModel { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;
    public AiRateLimitOptions RateLimit { get; set; } = new();
}

public sealed class AiRateLimitOptions
{
    public bool Enabled { get; set; }
    public int MaxRequestsPerMinute { get; set; } = 60;
}
