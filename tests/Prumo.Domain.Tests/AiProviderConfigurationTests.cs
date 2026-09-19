using Prumo.Infrastructure.Ai;

namespace Prumo.Domain.Tests;

public sealed class AiProviderConfigurationTests
{
    [Theory]
    [InlineData("Gemini")]
    [InlineData("OpenAI")]
    [InlineData("Http")]
    public void Supported_provider_configuration_is_valid(string provider)
    {
        var settings = new AiProviderOptions
        {
            Provider = provider,
            ApiKey = provider.Equals("Http", StringComparison.OrdinalIgnoreCase) ? string.Empty : "test-key",
            Endpoint = provider.Equals("Http", StringComparison.OrdinalIgnoreCase) ? "https://example.test/ai" : string.Empty,
            Model = "test-model"
        };

        Assert.True(new AiProviderOptionsValidator().Validate(null, settings).Succeeded);
    }

    [Fact]
    public void Invalid_configuration_fails_without_echoing_credentials()
    {
        var settings = new AiProviderOptions { Provider = "Http", ApiKey = "private-test-key", Endpoint = "invalid", TimeoutSeconds = 0 };
        var result = new AiProviderOptionsValidator().Validate(null, settings);
        Assert.True(result.Failed);
        Assert.Contains("Endpoint", result.FailureMessage);
        Assert.Contains("Model", result.FailureMessage);
        Assert.Contains("TimeoutSeconds", result.FailureMessage);
        Assert.DoesNotContain(settings.ApiKey, result.FailureMessage);
    }

    [Fact]
    public void Openai_requires_only_the_shared_configuration()
    {
        var settings = new AiProviderOptions { Provider = "OpenAI", ApiKey = "document-key", Model = "document-model" };
        Assert.True(new AiProviderOptionsValidator().Validate(null, settings).Succeeded);
    }
}
