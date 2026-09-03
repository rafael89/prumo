using System.Globalization;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Application.Radar;

public sealed class PortfolioDocumentAssessmentService
{
    private readonly RadarPriorityCalculator priorityCalculator = new();

    public PortfolioDocumentAssessment Assess(Guid portfolioId, PortfolioPosition position, DocumentAnalysis analysis, RadarItemStatus status = RadarItemStatus.Unread)
    {
        var priority = priorityCalculator.Calculate(analysis);
        var requiresAttention = RadarAttentionPolicy.RequiresAttention(priority.Priority, analysis.RequiresAction, status);
        var weight = position.PortfolioWeight.ToString("F2", CultureInfo.GetCultureInfo("pt-BR"));
        var impact = requiresAttention
            ? $"{position.Asset?.Ticker} representa {weight}% da carteira. O evento requer atenção antes de novos aportes."
            : $"{position.Asset?.Ticker} representa {weight}% da carteira. O evento não exige ação imediata.";
        return new(portfolioId, position.AssetId, analysis.Id, position.CurrentValue, position.PortfolioWeight, requiresAttention, impact, priority.Priority, priority.Reasons, status);
    }
}

public sealed record PortfolioDocumentAssessment(Guid PortfolioId, Guid AssetId, Guid DocumentAnalysisId,
    decimal? CurrentValue, decimal CurrentWeight, bool RequiresAttention, string PortfolioImpactText,
    RadarPriority Priority, IReadOnlyList<RadarReason> Reasons, RadarItemStatus RadarStatus);
