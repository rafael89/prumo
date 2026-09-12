using Prumo.Domain.Enums;

namespace Prumo.Application.Documents;

/// <summary>Deterministic materiality rules shared by live validation and reconciliation.</summary>
public static class DocumentMaterialityNormalization
{
    public static MaterialityNormalizationResult Normalize(AnalysisImportance importance, AnalysisClassification classification, bool requiresAction, SuggestedAction suggestedAction, string summary, string impact, IEnumerable<MaterialityEventFacts>? events = null)
    {
        if (classification != AnalysisClassification.Distribution || !IsRoutineDistribution(summary, impact, events ?? Array.Empty<MaterialityEventFacts>()))
            return new(importance, requiresAction, suggestedAction, false, true);

        return new(AnalysisImportance.Informative, false, SuggestedAction.NoAction,
            importance != AnalysisImportance.Informative || requiresAction || suggestedAction != SuggestedAction.NoAction, true);
    }

    private static bool IsRoutineDistribution(string summary, string impact, IEnumerable<MaterialityEventFacts> events)
    {
        var text = string.Join(' ', new[] { summary, impact }.Concat(events.SelectMany(item => new[] { item.Summary }.Concat(item.Facts)))).ToLowerInvariant();
        var exceptionalSignals = new[] { "suspens", "redução", "reducao", "atras", "inadimpl", "corte", "extraordin", "mudança de política", "mudanca de politica", "prazo", "decisão", "decisao", "alteração relevante", "alteracao relevante", "não pagamento", "nao pagamento" };
        return !exceptionalSignals.Any(text.Contains);
    }
}

public sealed record MaterialityEventFacts(string Summary, IReadOnlyList<string> Facts);
public sealed record MaterialityNormalizationResult(AnalysisImportance Importance, bool RequiresAction, SuggestedAction SuggestedAction, bool Changed, bool Reconcilable);
