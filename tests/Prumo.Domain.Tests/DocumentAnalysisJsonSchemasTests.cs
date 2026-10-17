using System.Text.Json;
using Prumo.Infrastructure.Ai;

namespace Prumo.Domain.Tests;

public sealed class DocumentAnalysisJsonSchemasTests
{
    [Fact]
    public void Provider_schemas_share_the_same_document_contract()
    {
        var gemini = JsonSerializer.SerializeToElement(DocumentAnalysisJsonSchemas.CreateForGemini());
        var openAi = JsonSerializer.SerializeToElement(DocumentAnalysisJsonSchemas.CreateForOpenAi());

        Assert.Equal(PropertyNames(gemini.GetProperty("properties")), PropertyNames(openAi.GetProperty("properties")));
        Assert.Equal(StringValues(gemini.GetProperty("required")), StringValues(openAi.GetProperty("required")));

        var geminiEvent = EventItem(gemini);
        var openAiEvent = EventItem(openAi);
        Assert.Equal(PropertyNames(geminiEvent.GetProperty("properties")), PropertyNames(openAiEvent.GetProperty("properties")));
        Assert.Equal(StringValues(geminiEvent.GetProperty("required")), StringValues(openAiEvent.GetProperty("required")));
        Assert.Equal(
            StringValues(geminiEvent.GetProperty("properties").GetProperty("eventType").GetProperty("enum")),
            StringValues(openAiEvent.GetProperty("properties").GetProperty("eventType").GetProperty("enum")));
    }

    [Fact]
    public void Provider_schemas_keep_only_their_intentional_capability_differences()
    {
        var gemini = JsonSerializer.SerializeToElement(DocumentAnalysisJsonSchemas.CreateForGemini());
        var openAi = JsonSerializer.SerializeToElement(DocumentAnalysisJsonSchemas.CreateForOpenAi());

        Assert.False(gemini.TryGetProperty("additionalProperties", out _));
        Assert.False(EventItem(gemini).TryGetProperty("additionalProperties", out _));
        Assert.False(openAi.GetProperty("additionalProperties").GetBoolean());
        Assert.False(EventItem(openAi).GetProperty("additionalProperties").GetBoolean());

        var geminiDate = EventItem(gemini).GetProperty("properties").GetProperty("occurredAt");
        var openAiDate = EventItem(openAi).GetProperty("properties").GetProperty("occurredAt");
        Assert.Equal("date-time", geminiDate.GetProperty("format").GetString());
        Assert.False(openAiDate.TryGetProperty("format", out _));
        Assert.Equal(StringValues(geminiDate.GetProperty("type")), StringValues(openAiDate.GetProperty("type")));
    }

    [Fact]
    public void Financial_fact_schema_requires_supported_period_type()
    {
        var schema = JsonSerializer.SerializeToElement(DocumentAnalysisJsonSchemas.CreateForOpenAi());
        var periodType = schema.GetProperty("properties").GetProperty("financialFacts")
            .GetProperty("items").GetProperty("properties").GetProperty("periodType");

        Assert.Equal(new[] { "Annual", "PointInTime", "Quarterly", "Ttm" }, StringValues(periodType.GetProperty("enum")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Financial_fact_schema_limits_output_to_unscaled_units(bool openAi)
    {
        var schema = JsonSerializer.SerializeToElement(openAi
            ? DocumentAnalysisJsonSchemas.CreateForOpenAi()
            : DocumentAnalysisJsonSchemas.CreateForGemini());
        var unit = schema.GetProperty("properties").GetProperty("financialFacts")
            .GetProperty("items").GetProperty("properties").GetProperty("unit");

        Assert.Equal(new[] { "currency", "percent", "ratio" }, StringValues(unit.GetProperty("enum")));
    }
    private static JsonElement EventItem(JsonElement schema) => schema.GetProperty("properties").GetProperty("events").GetProperty("items");
    private static string[] PropertyNames(JsonElement element) => element.EnumerateObject().Select(property => property.Name).Order().ToArray();
    private static string[] StringValues(JsonElement element) => element.EnumerateArray().Select(value => value.GetString()!).Order().ToArray();
}
