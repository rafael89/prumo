using Microsoft.Extensions.Options;

namespace Prumo.Infrastructure.Ai;

public sealed class AiProviderOptionsValidator : IValidateOptions<AiProviderOptions>
{
    public ValidateOptionsResult Validate(string? name, AiProviderOptions options)
    {
        var errors = new List<string>();
        var provider = options.Provider.Trim().ToUpperInvariant();
        if (provider is not ("FAKE" or "GEMINI" or "OPENAI" or "HTTP"))
            errors.Add("AI:Provider deve ser Fake, Gemini, OpenAI ou Http.");
        if (options.TimeoutSeconds <= 0) errors.Add("AI:TimeoutSeconds deve ser positivo.");
        if (provider is "GEMINI" or "OPENAI" or "HTTP") ValidateProvider(options, "AI", errors);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateProvider(AiProviderOptions options, string prefix, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(options.Model)) errors.Add($"{prefix}:Model é obrigatório.");
        if (options.Provider.Trim().Equals("Http", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                errors.Add($"{prefix}:Endpoint deve ser uma URL HTTP(S) absoluta.");
        }
        else if (string.IsNullOrWhiteSpace(options.ApiKey)) errors.Add($"{prefix}:ApiKey é obrigatória.");
    }
}
