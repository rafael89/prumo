namespace Prumo.Infrastructure.Ai;

public static class DocumentAnalysisJsonSchemas
{
    private static readonly string[] ImportanceValues = ["CRITICAL", "RELEVANT", "INFORMATIVE", "NO_ACTION"];
    private static readonly string[] ClassificationValues = ["CORPORATE_ACTION", "FINANCIAL_RESULT", "DISTRIBUTION", "CAPITAL_RAISE", "ASSET_TRANSACTION", "GOVERNANCE", "OPERATIONAL", "LIQUIDATION", "OTHER"];
    private static readonly string[] SuggestedActionValues = ["MAINTAIN", "INCREASE_POSITION", "REDUCE_EXPOSURE", "EVALUATE_SALE", "MONITOR", "NO_ACTION", "OPEN_POSITION"];
    private static readonly string[] EventTypeValues = ["FINANCIAL_RESULT", "DISTRIBUTION", "CAPITAL_RAISE", "CORPORATE_ACTION", "ASSET_TRANSACTION", "GOVERNANCE", "OPERATIONAL", "LIQUIDATION", "OTHER"];
    private static readonly string[] RootRequired = ["importance", "classification", "requiresAction", "suggestedAction", "summary", "impact", "affectedAssets", "confidence", "events", "financialFacts", "attentionBasis", "attentionReason", "attentionEvidence"];
    private static readonly string[] EventRequired = ["eventType", "occurredAt", "summary", "facts", "affectedAssets", "radarDetails"];

    public static object CreateForGemini() => Create(strict: false, includeDateTimeFormat: true);

    public static object CreateForOpenAi() => Create(strict: true, includeDateTimeFormat: false);

    private static Dictionary<string, object> Create(bool strict, bool includeDateTimeFormat)
    {
        var occurredAt = new Dictionary<string, object> { ["type"] = new[] { "string", "null" } };
        if (includeDateTimeFormat) occurredAt["format"] = "date-time";

        var eventSchema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["eventType"] = EnumSchema(EventTypeValues),
                ["occurredAt"] = occurredAt,
                ["summary"] = StringSchema(),
                ["facts"] = StringArraySchema(),
                ["affectedAssets"] = StringArraySchema(),
                ["radarDetails"] = RadarDetailsSchema(strict)
            },
            ["required"] = EventRequired
        };
        if (strict) eventSchema["additionalProperties"] = false;

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["importance"] = EnumSchema(ImportanceValues),
                ["attentionBasis"] = EnumSchema(["CHANGE", "DEADLINE", "DECISION", "NONE", "UNKNOWN"]),
                ["attentionReason"] = StringSchema(),
                ["attentionEvidence"] = StringSchema(),
                ["classification"] = EnumSchema(ClassificationValues),
                ["requiresAction"] = new Dictionary<string, object> { ["type"] = "boolean" },
                ["suggestedAction"] = EnumSchema(SuggestedActionValues),
                ["summary"] = StringSchema(),
                ["impact"] = StringSchema(),
                ["affectedAssets"] = StringArraySchema(),
                ["confidence"] = new Dictionary<string, object> { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 },
                ["events"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = eventSchema },
                ["financialFacts"] = FinancialFactsSchema()
            },
            ["required"] = RootRequired
        };
        if (strict) schema["additionalProperties"] = false;
        return schema;
    }

    private static Dictionary<string, object> RadarDetailsSchema(bool strict)
    {
        string[] fields = ["operationId", "stage", "deadline", "amount", "outstandingDebt", "restriction", "audience", "contractTerms", "evidence", "importance", "impactDirection", "applicability", "attentionBasis", "attentionReason", "attentionEvidence", "consequenceEvidence", "urgencyEvidence", "noveltyCandidate"];
        var schema = new Dictionary<string, object>
        {
            ["type"] = new[] { "object", "null" },
            ["properties"] = fields.ToDictionary(field => field, _ => (object)new Dictionary<string, object> { ["type"] = new[] { "string", "null" } }),
            ["required"] = strict ? fields : Array.Empty<string>()
        };
        if (strict) schema["additionalProperties"] = false;
        return schema;
    }

    private static Dictionary<string, object> EnumSchema(string[] values) => new() { ["type"] = "string", ["enum"] = values };
    private static Dictionary<string, object> StringSchema() => new() { ["type"] = "string" };
    private static Dictionary<string, object> StringArraySchema() => new() { ["type"] = "array", ["items"] = StringSchema() };
    private static Dictionary<string, object> FinancialFactsSchema() => new()
    {
        ["type"] = "array",
        ["items"] = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["metric"] = StringSchema(), ["value"] = new Dictionary<string, object> { ["type"] = new[] { "number", "null" } }, ["referencePeriod"] = StringSchema(), ["periodType"] = EnumSchema(["Annual", "Quarterly", "Ttm", "PointInTime"]), ["currency"] = new Dictionary<string, object> { ["type"] = new[] { "string", "null" } }, ["unit"] = EnumSchema(["currency", "percent", "ratio"]), ["evidence"] = StringSchema(), ["confidence"] = new Dictionary<string, object> { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 }
            },
            ["required"] = new[] { "metric", "value", "referencePeriod", "periodType", "currency", "unit", "evidence", "confidence" }
        }
    };
}
