using System.Text.Json;
using Lodira.Application.Abstractions;
using Lodira.Domain.Enums;

namespace Lodira.Infrastructure.Ai;

/// <summary>
/// Deterministic provider for Development and end-to-end validation.
/// </summary>
public sealed class FakeAiAnalysisService : IAiAnalysisService
{
    public string Model => "fake-e2e";
    public string PromptVersion => "fake-e2e-v1";

    public Task<AiAnalysisResult> AnalyzeAsync(
        string documentText,
        PortfolioAnalysisContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = new AiAnalysisResult(
            AnalysisImportance.Informative,
            AnalysisClassification.Other,
            false,
            SuggestedAction.NoAction,
            "Documento processado com sucesso.",
            "Análise de teste E2E.",
            "Nenhum impacto determinado neste teste.",
            string.IsNullOrWhiteSpace(context.Ticker) ? Array.Empty<string>() : new[] { context.Ticker },
            1.0m,
            string.Empty);

        return Task.FromResult(result with
        {
            RawResponse = JsonSerializer.Serialize(new
            {
                provider = "fake",
                ticker = context.Ticker,
                result.Importance,
                result.Classification,
                result.RequiresAction,
                result.SuggestedAction,
                result.Summary,
                result.Impact,
                result.PortfolioImpact,
                result.AffectedAssets,
                result.Confidence
            })
        });
    }
}
