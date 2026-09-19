using Prumo.Infrastructure.Ai;

namespace Prumo.Domain.Tests;

public sealed class AiProviderConfigurationTests
{
    [Theory]
    [InlineData("Gemini", "Gemini")]
    [InlineData("OpenAI", "Deterministic")]
    [InlineData("Fake", "Deterministic")]
    [InlineData("Http", "Http")]
    public void Automatic_contribution_provider_matches_supported_capabilities(string provider, string expected)
        => Assert.Equal(expected, new AiProviderOptions { Provider = provider }.ContributionProvider);

    [Fact]
    public void Openai_documents_do_not_require_an_unrelated_http_endpoint()
    {
        var settings = new AiProviderOptions { Provider = "OpenAI", ApiKey = "test-key", Model = "document-model" };
        Assert.True(new AiProviderOptionsValidator().Validate(null, settings).Succeeded);
        settings.Contribution.Provider = "OpenAI";
        Assert.True(new AiProviderOptionsValidator().Validate(null, settings).Failed);
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
    public void Contribution_overrides_do_not_change_document_settings()
    {
        var settings = new AiProviderOptions { Provider = "OpenAI", ApiKey = "document-key", Model = "document-model" };
        settings.Contribution = new() { Provider = "Gemini", ApiKey = "contribution-key", Model = "contribution-model" };
        Assert.True(new AiProviderOptionsValidator().Validate(null, settings).Succeeded);
        var contribution = settings.ForContribution();
        Assert.Equal("contribution-key", contribution.ApiKey);
        Assert.Equal("contribution-model", contribution.Model);
        Assert.Equal("document-key", settings.ApiKey);
        Assert.Equal("document-model", settings.Model);
    }

    [Fact]
    public void Gemini_cannot_silently_reuse_another_providers_key()
    {
        var settings = new AiProviderOptions { Provider = "OpenAI", ApiKey = "document-key", Model = "model" };
        settings.Contribution.Provider = "Gemini";
        Assert.True(new AiProviderOptionsValidator().Validate(null, settings).Failed);
    }

    [Fact]
    public void Custom_contribution_endpoint_does_not_inherit_another_providers_credentials()
    {
        var settings = new AiProviderOptions { Provider = "OpenAI", ApiKey = "document-key", Model = "document-model" };
        settings.Contribution = new() { Provider = "Http", Endpoint = "https://example.test/contributions", Model = "custom-model" };
        Assert.True(new AiProviderOptionsValidator().Validate(null, settings).Succeeded);
        Assert.Empty(settings.ForContribution().ApiKey);
    }
}
