using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Application.Radar;

/// <summary>Deterministic Radar scoring. Confidence explains uncertainty; it never raises severity.</summary>
public sealed class RadarPriorityCalculator
{
    public RadarPriorityResult Calculate(DocumentAnalysis analysis)
    {
        var score = analysis.Importance switch
        {
            AnalysisImportance.Critical => 40,
            AnalysisImportance.Relevant => 20,
            _ => 0
        };
        var reasons = new List<RadarReason>();

        if (analysis.Importance == AnalysisImportance.Critical) reasons.Add(RadarReason.HighImportance);
        else if (analysis.Importance == AnalysisImportance.Relevant) reasons.Add(RadarReason.MediumImportance);
        if (analysis.RequiresAction) { score += 30; reasons.Add(RadarReason.RequiresAction); }

        score += analysis.Classification switch
        {
            AnalysisClassification.CapitalRaise => 15,
            AnalysisClassification.Liquidation => 20,
            AnalysisClassification.CorporateAction or AnalysisClassification.FinancialResult => 10,
            AnalysisClassification.Distribution => 5,
            _ => 0
        };
        if (analysis.Classification is AnalysisClassification.CapitalRaise or AnalysisClassification.Liquidation or AnalysisClassification.CorporateAction)
            reasons.Add(RadarReason.RelevantCorporateEvent);
        if (analysis.SuggestedAction is SuggestedAction.ReduceExposure or SuggestedAction.EvaluateSale) score += 10;
        else if (analysis.SuggestedAction == SuggestedAction.Monitor) score += 5;
        if (analysis.AffectedAssets.Count > 0) { score += 5; reasons.Add(RadarReason.AffectsPortfolioAsset); }
        if (analysis.Confidence < 0.60m) reasons.Add(RadarReason.ReducedConfidence);

        var priority = score >= 80 ? RadarPriority.Critical : score >= 50 ? RadarPriority.High : score >= 20 ? RadarPriority.Medium : RadarPriority.Low;
        return new RadarPriorityResult(priority, score, reasons);
    }
}
