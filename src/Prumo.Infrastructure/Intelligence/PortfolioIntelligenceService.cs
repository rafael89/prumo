using Microsoft.EntityFrameworkCore;
using Prumo.Application.Intelligence;
using Prumo.Application.Performance;
using Prumo.Application.Radar;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Performance;

namespace Prumo.Infrastructure.Intelligence;

public sealed class PortfolioIntelligenceService(PrumoDbContext db, PortfolioPerformanceService performance)
{
    private const decimal SignificantPriceChange = 0.05m;

    public async Task<PortfolioIntelligenceDto?> GetAsync(Guid portfolioId, CancellationToken ct = default)
    {
        var portfolio = await db.Portfolios.AsNoTracking().Include(x => x.Positions).ThenInclude(x => x.Asset).SingleOrDefaultAsync(x => x.Id == portfolioId, ct);
        if (portfolio is null) return null;
        var generatedAt = DateTimeOffset.UtcNow;
        var targets = await db.TargetAllocations.AsNoTracking().Where(x => x.PortfolioId == portfolioId).ToListAsync(ct);
        var assessments = await db.PortfolioAssessments.AsNoTracking().AsSplitQuery().Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets).Include(x => x.InvestmentEvent!).ThenInclude(x => x.Evidence)
            .Include(x => x.InvestmentEvent!).ThenInclude(x => x.DocumentAnalysis!).ThenInclude(x => x.Document)
            .Where(x => x.PortfolioId == portfolioId && x.Status != AssessmentStatus.Closed).ToListAsync(ct);
        var transactions = await db.PortfolioTransactions.AsNoTracking().Include(x => x.Items).Where(x => x.PortfolioId == portfolioId).ToListAsync(ct);
        var prices = await db.AssetPrices.AsNoTracking().Where(x => portfolio.Positions.Select(p => p.AssetId).Contains(x.AssetId)).OrderByDescending(x => x.PriceDate).ThenByDescending(x => x.FetchedAt).ToListAsync(ct);
        var performanceData = await performance.GetAsync(portfolioId, null, null, ct);
        var signals = new List<PortfolioIntelligenceSignal>();
        AddAllocationSignals(portfolio.Positions, targets, prices, signals);
        AddAssessmentSignals(assessments, signals);
        AddPriceSignals(portfolio.Positions, prices, signals);
        AddPerformanceSignals(performanceData, signals);
        AddDecisionSignals(portfolio.Positions, prices, transactions, signals);

        var ordered = signals.OrderByDescending(x => x.Priority).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        var attention = ordered.Take(10).ToList();
        var summary = attention.Count == 0 ? "Nenhuma mudança relevante detectada com os dados atuais." : $"{attention.Count} sinal(is) merece(m) atenção na carteira.";
        return new(generatedAt, summary, attention, ordered.Where(x => x.Type is IntelligenceSignalType.PriceIncrease or IntelligenceSignalType.PriceDecrease).ToList(), ordered.Where(x => x.Type == IntelligenceSignalType.AllocationDeviation).ToList(), ordered.Where(x => x.Type == IntelligenceSignalType.DecisionFollowUp).ToList(), ordered.Where(x => x.AssetId.HasValue).ToList(), ordered.Where(x => x.Type == IntelligenceSignalType.CoverageWarning).ToList());
    }

    private static void AddAllocationSignals(IEnumerable<Domain.Entities.PortfolioPosition> positions, IReadOnlyList<Domain.Entities.TargetAllocation> targets, IReadOnlyList<Domain.Entities.AssetPrice> prices, List<PortfolioIntelligenceSignal> signals)
    {
        var values = positions.Select(p => (p, value: prices.Where(x => x.AssetId == p.AssetId).OrderByDescending(x => x.PriceDate).FirstOrDefault() is { } price ? p.Quantity * price.Close : (decimal?)null)).ToList();
        var knownTotal = values.Where(x => x.value.HasValue).Sum(x => x.value!.Value);
        foreach (var group in values.GroupBy(x => x.p.Asset?.AssetClass))
        {
            var target = targets.FirstOrDefault(x => x.AssetClass == group.Key);
            var value = group.Where(x => x.value.HasValue).Sum(x => x.value!.Value);
            if (target is null || knownTotal <= 0) continue;
            var actual = value / knownTotal * 100;
            var gap = actual - target.TargetPercentage;
            if (Math.Abs(gap) < 2) continue;
            var direction = gap > 0 ? "acima" : "abaixo";
            signals.Add(new($"allocation:{group.Key}", IntelligenceSignalType.AllocationDeviation, 60 + (int)Math.Min(Math.Abs(gap), 30), $"Alocação {direction} do alvo", $"A classe {group.Key} está {Math.Abs(gap):0.##} pontos percentuais {direction} do alvo conhecido.", null, null, IntelligenceSignalQuality.Safe, [new("TargetAllocation", target.Id.ToString(), group.Key?.ToString() ?? "Classe")]));
        }
    }

    private static void AddAssessmentSignals(IReadOnlyList<Domain.Entities.PortfolioAssessment> assessments, List<PortfolioIntelligenceSignal> signals)
    {
        foreach (var assessment in assessments.Where(x => x.InvestmentEvent is not null && RadarEligibilityPolicy.IsEligible(x.InvestmentEvent))
            .OrderByDescending(x => x.AttentionPriority).ThenByDescending(x => x.GeneratedAt).Take(10))
        {
            var eventItem = assessment.InvestmentEvent;
            if (eventItem is null) continue;
            var asset = eventItem.AffectedAssets.FirstOrDefault();
            var evidence = new List<IntelligenceEvidence> { new("PortfolioAssessment", assessment.Id.ToString(), assessment.Summary), new("InvestmentEvent", eventItem.Id.ToString(), eventItem.Summary) };
            if (eventItem.Evidence.FirstOrDefault() is { } source) evidence.Add(new("Document", source.DocumentId.ToString(), source.Reference));
            var priority = assessment.AttentionPriority switch { AttentionPriority.Critical => 100, AttentionPriority.High => 90, AttentionPriority.Medium => 60, _ => 30 };
            if (RadarAttentionNowPolicy.RequiresAttention(assessment))
                signals.Add(new($"radar:{assessment.Id}", IntelligenceSignalType.HighAttentionRadar, priority, "Atenção no Radar", assessment.Summary, asset?.Id, asset?.Ticker, IntelligenceSignalQuality.Safe, evidence));
            if (eventItem.OccurredAt >= DateTimeOffset.UtcNow.AddDays(-90))
                signals.Add(new($"event:{eventItem.Id}", IntelligenceSignalType.RecentRelevantEvent, Math.Max(40, priority - 10), "Evento recente relevante", eventItem.Summary, asset?.Id, asset?.Ticker, IntelligenceSignalQuality.Safe, evidence));
        }
    }

    private static void AddPriceSignals(IEnumerable<Domain.Entities.PortfolioPosition> positions, IReadOnlyList<Domain.Entities.AssetPrice> prices, List<PortfolioIntelligenceSignal> signals)
    {
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-30));
        foreach (var position in positions)
        {
            var history = prices.Where(x => x.AssetId == position.AssetId).ToList();
            var end = history.OrderByDescending(x => x.PriceDate).FirstOrDefault();
            var start = history.Where(x => x.PriceDate <= cutoff).OrderByDescending(x => x.PriceDate).FirstOrDefault();
            if (start is null || end is null || start.Close <= 0) continue;
            var change = end.Close / start.Close - 1;
            if (Math.Abs(change) < SignificantPriceChange) continue;
            var type = change > 0 ? IntelligenceSignalType.PriceIncrease : IntelligenceSignalType.PriceDecrease;
            signals.Add(new($"price:{position.AssetId}:30d", type, 50, "Variação de preço relevante", $"{position.Asset?.Ticker} variou {change:P2} nos últimos 30 dias.", position.AssetId, position.Asset?.Ticker, IntelligenceSignalQuality.Safe, [new("AssetPrice", $"{start.PriceDate}:{end.PriceDate}", "Período de preço")]));
        }
    }

    private static void AddPerformanceSignals(PortfolioPerformanceDto? performance, List<PortfolioIntelligenceSignal> signals)
    {
        if (performance is null) return;
        foreach (var asset in performance.Assets.Where(x => x.UnrealizedPnl.Coverage != PerformanceCoverage.Unavailable))
        {
            if (asset.UnrealizedPnl.Coverage == PerformanceCoverage.Partial)
                signals.Add(new($"coverage:pnl:{asset.AssetId}", IntelligenceSignalType.CoverageWarning, 25, "P&L com cobertura parcial", $"O P&L não realizado conhecido de {asset.Ticker} depende de histórico parcial.", asset.AssetId, asset.Ticker, IntelligenceSignalQuality.Qualified, [new("PerformanceMetric", "UnrealizedPnl", "P&L não realizado")]));
        }
    }

    private static void AddDecisionSignals(IReadOnlyList<Domain.Entities.PortfolioPosition> positions, IReadOnlyList<Domain.Entities.AssetPrice> prices, IReadOnlyList<Domain.Entities.PortfolioTransaction> transactions, List<PortfolioIntelligenceSignal> signals)
    {
        foreach (var position in positions)
        {
            var purchase = transactions.Where(x => x.Type == PortfolioTransactionType.Buy && x.Items.Any(i => i.AssetId == position.AssetId)).OrderByDescending(x => x.OccurredAt).FirstOrDefault();
            if (purchase is null) continue;
            var start = prices.Where(x => x.AssetId == position.AssetId && x.PriceDate <= DateOnly.FromDateTime(purchase.OccurredAt.UtcDateTime.Date)).OrderByDescending(x => x.PriceDate).FirstOrDefault();
            var end = prices.Where(x => x.AssetId == position.AssetId).OrderByDescending(x => x.PriceDate).FirstOrDefault();
            if (start is null || end is null) continue;
            var change = end.Close / start.Close - 1;
            signals.Add(new($"decision:{purchase.Id}", IntelligenceSignalType.DecisionFollowUp, 35, "Acompanhamento de decisão", $"Desde a última compra de {position.Asset?.Ticker}, o preço variou {change:P2}. Isso é variação de preço, não retorno do investidor.", position.AssetId, position.Asset?.Ticker, IntelligenceSignalQuality.Safe, [new("PortfolioTransaction", purchase.Id.ToString(), "Última compra"), new("AssetPrice", $"{start.PriceDate}:{end.PriceDate}", "Período de preço")]));
        }
    }

}
