using Prumo.Application.Abstractions;
using Prumo.Domain.Enums;

namespace Prumo.Application.Documents;

public sealed class AiAnalysisValidator
{
    public AiAnalysisResult Validate(AiAnalysisPayload payload, string rawResponse)
    {
        if (payload is null) throw new ArgumentNullException(nameof(payload));
        if (string.IsNullOrWhiteSpace(payload.Importance) || !Enum.TryParse(payload.Importance, true, out AnalysisImportance importance)) throw new InvalidOperationException("Importance invalida ou ausente.");
        if (string.IsNullOrWhiteSpace(payload.Classification) || !Enum.TryParse(payload.Classification, true, out AnalysisClassification classification)) throw new InvalidOperationException("Classification invalida ou ausente.");
        if (!payload.RequiresAction.HasValue) throw new InvalidOperationException("RequiresAction ausente.");
        if (string.IsNullOrWhiteSpace(payload.SuggestedAction) || !Enum.TryParse(payload.SuggestedAction, true, out SuggestedAction suggestedAction)) throw new InvalidOperationException("SuggestedAction invalida ou ausente.");
        if (string.IsNullOrWhiteSpace(payload.Summary)) throw new InvalidOperationException("Summary ausente.");
        if (string.IsNullOrWhiteSpace(payload.Impact)) throw new InvalidOperationException("Impact ausente.");
        if (string.IsNullOrWhiteSpace(payload.PortfolioImpact)) throw new InvalidOperationException("PortfolioImpact ausente.");
        if (!payload.Confidence.HasValue || payload.Confidence is < 0 or > 1) throw new InvalidOperationException("Confidence deve estar entre 0 e 1.");
        if (payload.AffectedAssets is null) throw new InvalidOperationException("AffectedAssets ausente.");

        var summary = payload.Summary.Trim();
        var impact = payload.Impact.Trim();
        var portfolioImpact = payload.PortfolioImpact.Trim();
        if (summary.Length == 0) throw new InvalidOperationException("Summary ausente.");
        if (impact.Length == 0) throw new InvalidOperationException("Impact ausente.");
        if (portfolioImpact.Length == 0) throw new InvalidOperationException("PortfolioImpact ausente.");

        return new AiAnalysisResult(
            importance,
            classification,
            payload.RequiresAction.Value,
            suggestedAction,
            summary,
            impact,
            portfolioImpact,
            payload.AffectedAssets.Where(asset => !string.IsNullOrWhiteSpace(asset)).Select(asset => asset.Trim().ToUpperInvariant()).Distinct().ToArray(),
            payload.Confidence.Value,
            LimitRawResponse(rawResponse));
    }

    private static string LimitRawResponse(string rawResponse) => string.IsNullOrWhiteSpace(rawResponse)
        ? throw new InvalidOperationException("Resposta original ausente.")
        : rawResponse.Length <= 100_000 ? rawResponse : rawResponse[..100_000];
}
