using Lodira.Application.Abstractions;
using Lodira.Domain.Enums;

namespace Lodira.Application.Documents;

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
        if (!payload.Confidence.HasValue || payload.Confidence is < 0 or > 1) throw new InvalidOperationException("Confidence deve estar entre 0 e 1.");
        if (payload.AffectedAssets is null) throw new InvalidOperationException("AffectedAssets ausente.");

        var summary = payload.Summary.Trim();
        var impact = payload.Impact.Trim();
        var portfolioImpact = string.IsNullOrWhiteSpace(payload.PortfolioImpact) ? "Impacto identificado para o ativo." : payload.PortfolioImpact.Trim();
        if (summary.Length == 0) throw new InvalidOperationException("Summary ausente.");
        if (impact.Length == 0) throw new InvalidOperationException("Impact ausente.");

        var events = (payload.Events ?? Array.Empty<AiInvestmentEventPayload>()).Select(eventPayload =>
        {
            if (string.IsNullOrWhiteSpace(eventPayload.EventType) || !Enum.TryParse(eventPayload.EventType, true, out InvestmentEventType eventType)) throw new InvalidOperationException("EventType inválido ou ausente.");
            if (string.IsNullOrWhiteSpace(eventPayload.Summary)) throw new InvalidOperationException("Resumo do evento ausente.");
            return new AiInvestmentEventResult(eventType, eventPayload.OccurredAt?.ToUniversalTime(), eventPayload.Summary.Trim(), (eventPayload.Facts ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToArray(), (eventPayload.AffectedAssets ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToUpperInvariant()).Distinct().ToArray());
        }).ToArray();

        if (classification == AnalysisClassification.Distribution && IsRoutineDistribution(summary, impact, events))
        {
            importance = AnalysisImportance.Informative;
            suggestedAction = SuggestedAction.NoAction;
            payload = payload with { RequiresAction = false };
        }

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
            LimitRawResponse(rawResponse), events);
    }

    private static string LimitRawResponse(string rawResponse) => string.IsNullOrWhiteSpace(rawResponse)
        ? throw new InvalidOperationException("Resposta original ausente.")
        : rawResponse.Length <= 100_000 ? rawResponse : rawResponse[..100_000];

    private static bool IsRoutineDistribution(string summary, string impact, IReadOnlyList<AiInvestmentEventResult> events)
    {
        var text = string.Join(' ', new[] { summary, impact }
            .Concat(events.SelectMany(item => new[] { item.Summary }.Concat(item.Facts))))
            .ToLowerInvariant();
        var exceptionalSignals = new[]
        {
            "suspens", "redução", "reducao", "atras", "inadimpl", "corte", "extraordin",
            "mudança de política", "mudanca de politica", "prazo", "decisão", "decisao",
            "alteração relevante", "alteracao relevante", "não pagamento", "nao pagamento"
        };
        return !exceptionalSignals.Any(text.Contains);
    }
}
