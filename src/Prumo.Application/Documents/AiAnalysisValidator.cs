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
        if (!payload.Confidence.HasValue || payload.Confidence is < 0 or > 1) throw new InvalidOperationException("Confidence deve estar entre 0 e 1.");
        if (payload.AffectedAssets is null) throw new InvalidOperationException("AffectedAssets ausente.");

        var summary = payload.Summary.Trim();
        var impact = payload.Impact.Trim();
        var portfolioImpact = string.IsNullOrWhiteSpace(payload.PortfolioImpact) ? "Impacto identificado para o ativo." : payload.PortfolioImpact.Trim();
        if (summary.Length == 0) throw new InvalidOperationException("Summary ausente.");
        if (impact.Length == 0) throw new InvalidOperationException("Impact ausente.");

        var events = (payload.Events ?? Array.Empty<AiInvestmentEventPayload>())
            .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Summary) && Enum.TryParse<InvestmentEventType>(e.EventType, true, out var type) && Enum.IsDefined(type))
            .Select(eventPayload =>
        {
            if (string.IsNullOrWhiteSpace(eventPayload.EventType) || !Enum.TryParse(eventPayload.EventType, true, out InvestmentEventType eventType)) throw new InvalidOperationException("EventType inválido ou ausente.");
            if (string.IsNullOrWhiteSpace(eventPayload.Summary)) throw new InvalidOperationException("Resumo do evento ausente.");
            return new AiInvestmentEventResult(eventType, eventPayload.OccurredAt?.ToUniversalTime(), eventPayload.Summary!.Trim(), (eventPayload.Facts ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToArray(), (eventPayload.AffectedAssets ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToUpperInvariant()).Distinct().ToArray(), eventPayload.RadarDetails);
        }).ToArray();
        var normalizedFinancialFacts = (payload.FinancialFacts ?? Array.Empty<AiFinancialFactPayload>())
            .Where(fact => fact is not null)
            .Select(fact => (Payload: fact, Result: DocumentFinancialFactNormalizer.NormalizeWithReason(fact)))
            .ToArray();
        var financialFacts = normalizedFinancialFacts.Where(x => x.Result.IsAccepted).Select(x => x.Result.Fact!).GroupBy(x => new { x.Metric, x.ReferencePeriod, x.PeriodType }).Select(x => x.OrderByDescending(f => f.Confidence).First()).ToArray();
        var rejectedFinancialFacts = normalizedFinancialFacts.Where(x => !x.Result.IsAccepted).Select(x => new AiFinancialFactRejection(x.Payload.Metric, x.Payload.Value, x.Payload.Unit, x.Payload.Currency, x.Payload.ReferencePeriod, x.Payload.PeriodType, x.Result.RejectionReason!)).ToArray();

        var normalization = DocumentMaterialityNormalization.Normalize(
            importance, classification, payload.RequiresAction.Value, suggestedAction, summary, impact,
            events.Select(item => new MaterialityEventFacts(item.Summary, item.Facts)));
        var basis = payload.AttentionBasis?.Trim().ToUpperInvariant();
        if (basis is not null && basis is not ("CHANGE" or "DEADLINE" or "DECISION" or "NONE" or "UNKNOWN"))
            throw new InvalidOperationException("AttentionBasis inválido.");
        var confirmed = (basis is "CHANGE" or "DEADLINE" or "DECISION")
            && !string.IsNullOrWhiteSpace(payload.AttentionReason) && !string.IsNullOrWhiteSpace(payload.AttentionEvidence);
        if (basis is not null && !confirmed)
            normalization = normalization with { RequiresAction = false, SuggestedAction = normalization.Importance == AnalysisImportance.Relevant ? SuggestedAction.Monitor : SuggestedAction.NoAction };
        importance = normalization.Importance;
        suggestedAction = normalization.SuggestedAction;
        payload = payload with { RequiresAction = normalization.RequiresAction };

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
            LimitRawResponse(rawResponse), events, null, financialFacts, rejectedFinancialFacts,
            basis, payload.AttentionReason?.Trim(), payload.AttentionEvidence?.Trim());
    }

    private static string LimitRawResponse(string rawResponse) => string.IsNullOrWhiteSpace(rawResponse)
        ? throw new InvalidOperationException("Resposta original ausente.")
        : rawResponse.Length <= 100_000 ? rawResponse : rawResponse[..100_000];

}
