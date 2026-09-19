namespace Prumo.Infrastructure.Ai;

public sealed class AiProviderOptions
{
    public string Provider { get; set; } = "Http";
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string FallbackModel { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;
    public AiRateLimitOptions RateLimit { get; set; } = new();
    public ContributionAiOptions Contribution { get; set; } = new();

    public string ContributionProvider => Contribution.Provider.Trim().ToUpperInvariant() switch
    {
        "" or "AUTO" => Provider.Trim().ToUpperInvariant() switch
        {
            "GEMINI" => "Gemini",
            "HTTP" => "Http",
            _ => "Deterministic"
        },
        "GEMINI" => "Gemini",
        "HTTP" => "Http",
        "DETERMINISTIC" => "Deterministic",
        _ => Contribution.Provider
    };

    public AiProviderOptions ForContribution()
    {
        var inherit = string.Equals(Provider.Trim(), ContributionProvider, StringComparison.OrdinalIgnoreCase);
        string Resolve(string value, string documentValue) => !string.IsNullOrWhiteSpace(value) ? value : inherit ? documentValue : string.Empty;
        return new()
        {
            Provider = ContributionProvider,
            Endpoint = Resolve(Contribution.Endpoint, Endpoint),
            ApiKey = Resolve(Contribution.ApiKey, ApiKey),
            Model = Resolve(Contribution.Model, Model),
            FallbackModel = Resolve(Contribution.FallbackModel, FallbackModel),
            TimeoutSeconds = TimeoutSeconds,
            RateLimit = RateLimit,
            Contribution = Contribution
        };
    }
}

public sealed class ContributionAiOptions
{
    public string Provider { get; set; } = "Auto";
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string FallbackModel { get; set; } = string.Empty;
    public string ThinkingLevel { get; set; } = "medium";
}

public sealed class AiRateLimitOptions
{
    public bool Enabled { get; set; }
    public int MaxRequestsPerMinute { get; set; } = 60;
    public int MaxTokensPerMinute { get; set; }
    public int MaxRequestsPerDay { get; set; }
}
