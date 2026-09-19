using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prumo.Application.Abstractions;
using Prumo.Application.Contributions;
using Prumo.Application.Explainability;
using Prumo.Application.Portfolio;
using Prumo.Api.Services;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Domain.Rules;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Reconciliation;
using Prumo.Infrastructure.MarketData;

namespace Prumo.Api.Services;

public sealed class ContributionContextBuilder(PrumoDbContext db, PortfolioAssessmentCurrentService current, TransactionReconciliationService? canonical = null)
{
    private static readonly TimeSpan RecentContributionWindow = TimeSpan.FromDays(90);

    public async Task<ContributionContext> BuildAsync(Portfolio portfolio, IReadOnlyList<TargetAllocation> targets, decimal amount, CancellationToken ct)
    {
        var portfolioId = portfolio.Id;
        var recentSince = DateTimeOffset.UtcNow.Subtract(RecentContributionWindow);
        var canonicalTransactions = await (canonical ?? new TransactionReconciliationService(db)).Canonical(portfolioId, ct);
        var purchaseHistoryByAsset = canonicalTransactions
            .Where(x => x.Type == nameof(PortfolioTransactionType.Buy))
            .SelectMany(transaction => transaction.Items.Select(item => item.AssetId))
            .GroupBy(assetId => assetId)
            .ToDictionary(group => group.Key, group => group.Count());
        var recentPurchases = canonicalTransactions
            .Where(x => x.Type == nameof(PortfolioTransactionType.Buy) && x.OccurredAt >= recentSince)
            .ToList();
        var recentByAsset = recentPurchases.SelectMany(transaction => transaction.Items.Select(item => new
            {
                item.AssetId,
                Amount = item.Quantity * item.UnitPrice,
                transaction.OccurredAt
            }))
            .GroupBy(x => x.AssetId)
            .ToDictionary(x => x.Key, x => new
            {
                Amount = decimal.Round(x.Sum(item => item.Amount), 2),
                Count = x.Count(),
                LastOccurredAt = x.Max(item => item.OccurredAt)
            });
        var classTotals = portfolio.Positions.Where(x => x.Asset is not null && x.CurrentValue.HasValue)
            .GroupBy(x => x.Asset!.AssetClass)
            .ToDictionary(x => x.Key, x => x.Sum(position => position.CurrentValue!.Value));
        var sectorTotals = portfolio.Positions.Where(x => x.Asset is not null && x.CurrentValue.HasValue)
            .GroupBy(x => new { AssetClass = x.Asset!.AssetClass, Sector = NormalizeSector(x.Asset.Sector) })
            .ToDictionary(x => x.Key, x => x.Sum(position => position.CurrentValue!.Value));

        var health = new PortfolioHealthCalculator().Calculate(portfolio, targets);
        var requiresRebalance = health.AllocationDeviations.Any(x => x.Direction == AllocationDeviationDirection.Underweight);
        var total = portfolio.Positions.Sum(x => x.CurrentValue ?? 0);
        // Posições zeradas permanecem no cadastro/histórico, mas não podem
        // receber novos aportes até que uma importação as reative.
        var existingPositions = portfolio.Positions.Where(x => x.Asset is not null && x.CurrentValue is > 0)
            .Select(x =>
            {
                var asset = x.Asset!;
                var recent = recentByAsset.GetValueOrDefault(x.AssetId);
                var sector = NormalizeSector(asset.Sector);
                var sectorValue = sector.Length == 0 ? 0 : sectorTotals.GetValueOrDefault(new { AssetClass = asset.AssetClass, Sector = sector });
                var sectorWeight = sector.Length == 0 || classTotals.GetValueOrDefault(asset.AssetClass) <= 0
                    ? 100
                    : sectorValue / classTotals[asset.AssetClass] * 100m;
                var deviation = health.AllocationDeviations.FirstOrDefault(x => x.AssetClass == asset.AssetClass);
                var allocationNeedScore = requiresRebalance ? ScoreNeed(deviation?.TargetPercentage ?? 0, deviation?.CurrentPercentage ?? 0) : 0m;
                var fundType = SectorExposureSemantics.InferFundType(asset.AssetType, asset.Sector);
                var diversificationScore = SectorExposureSemantics.IsFundOfFunds(fundType, asset.Sector) || sector.Length == 0 ? 0 : Math.Clamp(100m - sectorWeight, 0, 100);
                var underweightScore = Math.Clamp(100m - (total == 0 ? 0 : x.CurrentValue!.Value / total * 100m), 0, 100);
                var recentPenalty = recent is null ? 0 : Math.Clamp(25m + recent.Amount / Math.Max(total, 1m) * 100m, 0, 100);
                var isEstablishing = recent is not null
                    && purchaseHistoryByAsset.GetValueOrDefault(x.AssetId) <= 1
                    && total > 0
                    && x.CurrentValue!.Value / total * 100m < 2m;
                return new GuidancePosition(x.AssetId, asset.Ticker, asset.AssetClass, x.CurrentValue!.Value,
                    total == 0 ? 0 : x.CurrentValue.Value / total * 100m, asset.AssetType, asset.Sector,
                    recent?.Amount ?? 0, recent?.Count ?? 0, recent?.LastOccurredAt, sectorWeight, true,
                    allocationNeedScore, diversificationScore, underweightScore, recentPenalty,
                    asset.SectorCategory, asset.GeographicExposureCoverage,
                    asset.GeographicExposures.Select(e => new KnownGeographicExposure(e.CountryCode, e.Percentage, e.Source.ToString(), e.SourceReference, e.ObservedAt)).ToArray(),
                    asset.EconomicCurrencyExposureCoverage,
                    asset.CurrencyExposures.Select(e => new KnownCurrencyExposure(e.CurrencyCode, e.Percentage, e.Source.ToString(), e.SourceReference, e.ObservedAt)).ToArray(),
                    asset.ListingCountryCode, asset.TradingCurrency, DocumentFinancialQualityResolver.Resolve(asset))
                {
                    FundType = fundType,
                    UnderlyingSectorExposure = SectorExposureSemantics.IsFundOfFunds(fundType, asset.Sector) ? UnderlyingSectorExposure.Unavailable : new(UnderlyingSectorExposureStatus.Available),
                    IsEstablishingPosition = isEstablishing
                };
            }).ToList();
        var positionAssetIds = portfolio.Positions.Select(x => x.AssetId).ToHashSet();
        var targetClasses = targets.Select(x => x.AssetClass).ToHashSet();
        var catalogCandidates = await db.Assets.AsNoTracking().AsSplitQuery()
            .Include(x => x.GeographicExposures).Include(x => x.CurrencyExposures)
            .Include(x => x.FundamentalSnapshots).Include(x => x.ValuationSnapshots).Include(x => x.DocumentFinancialFacts).Include(x => x.CvmFiiMonthlyObservations)
            .Where(x => x.IsActive && (targetClasses.Contains(x.AssetClass) || targetClasses.Contains(AssetClass.International) && x.AssetType == AssetType.Etf) && !positionAssetIds.Contains(x.Id))
            .OrderBy(x => x.Ticker)
            .ToListAsync(ct);
        var positions = existingPositions.Concat(catalogCandidates.Select(asset =>
        {
            var deviation = health.AllocationDeviations.FirstOrDefault(x => x.AssetClass == asset.AssetClass);
            var assetClass = asset.AssetType == AssetType.Etf ? AssetClass.International : asset.AssetClass;
            var fundType = SectorExposureSemantics.InferFundType(asset.AssetType, asset.Sector);
            return new GuidancePosition(asset.Id, asset.Ticker, assetClass, 0, 0, asset.AssetType, asset.Sector,
                HasPosition: false, AllocationNeedScore: requiresRebalance ? ScoreNeed(deviation?.TargetPercentage ?? 0, deviation?.CurrentPercentage ?? 0) : 0m,
                UnderweightScore: 100, SectorCategory: asset.SectorCategory,
                GeographicExposureCoverage: asset.GeographicExposureCoverage,
                GeographicExposures: asset.GeographicExposures.Select(e => new KnownGeographicExposure(e.CountryCode, e.Percentage, e.Source.ToString(), e.SourceReference, e.ObservedAt)).ToArray(),
                EconomicCurrencyExposureCoverage: asset.EconomicCurrencyExposureCoverage,
                CurrencyExposures: asset.CurrencyExposures.Select(e => new KnownCurrencyExposure(e.CurrencyCode, e.Percentage, e.Source.ToString(), e.SourceReference, e.ObservedAt)).ToArray(),
                ListingCountryCode: asset.ListingCountryCode, TradingCurrency: asset.TradingCurrency, FinancialSnapshot: DocumentFinancialQualityResolver.Resolve(asset))
            { FundType = fundType, UnderlyingSectorExposure = SectorExposureSemantics.IsFundOfFunds(fundType, asset.Sector) ? UnderlyingSectorExposure.Unavailable : new(UnderlyingSectorExposureStatus.Available) };
        })).ToList();
        var ids = positions.Select(x => x.AssetId).ToHashSet();
        var assessments = await db.PortfolioAssessments.AsNoTracking().AsSplitQuery()
            .Include(x => x.InvestmentEvent!).ThenInclude(x => x.AffectedAssets)
            .Include(x => x.InvestmentEvent!).ThenInclude(x => x.DocumentAnalysis)
            .Include(x => x.InvestmentEvent!).ThenInclude(x => x.Evidence)
            .Where(x => x.PortfolioId == portfolioId && x.Status != AssessmentStatus.Closed
                && (x.InvestmentEvent!.DocumentAnalysis!.Importance == AnalysisImportance.Critical
                    || x.InvestmentEvent.DocumentAnalysis.Importance == AnalysisImportance.Relevant)
                && x.InvestmentEvent.AffectedAssets.Any(a => ids.Contains(a.Id))).ToListAsync(ct);
        assessments = (await current.EnsureFreshAsync(portfolio, targets, assessments, ct)).ToList();

        var events = await db.InvestmentEvents.AsNoTracking().AsSplitQuery()
            .Include(x => x.AffectedAssets)
            .Include(x => x.Evidence)
            .Where(x => x.AffectedAssets.Any(a => ids.Contains(a.Id)))
            .OrderByDescending(x => x.OccurredAt ?? x.CreatedAt)
            .ToListAsync(ct);

        var inputs = assessments.Where(x => x.InvestmentEvent is not null)
            .SelectMany(x => x.InvestmentEvent!.AffectedAssets.Where(a => ids.Contains(a.Id)).Select(a => new GuidanceAssessment(
                a.Id, x.CapitalDecisionImpact, x.Status, x.InvestmentEvent.Evidence.FirstOrDefault()?.Reference, x.AssessmentVersion,
                x.AttentionPriority, x.ActionRequirement, x.Summary, x.WhyItMatters, x.SuggestedBehavior, ConfidenceValue(x.Confidence), x.Id))).ToList();
        var intelligenceContextKey = ContributionIntelligenceContextKeyCalculator.Calculate(inputs);
        var baseline = new ContributionGuidanceCalculator().Calculate(new ContributionGuidanceInput(portfolioId, amount, health, positions, inputs));
        var classBudgets = baseline.Allocations.GroupBy(x => x.AssetClass).ToDictionary(x => x.Key, x => x.Sum(a => a.Amount));
        var deviations = health.AllocationDeviations.GroupBy(x => x.AssetClass).ToDictionary(x => x.Key, x => x.First());
        var candidates = positions.Select(position =>
        {
            var contexts = inputs.Where(x => x.AssetId == position.AssetId).Select(x => new ContributionAssessmentContext(
                x.AttentionPriority.ToString(), x.ActionRequirement.ToString(), x.Impact.ToString(), x.Summary, x.WhyItMatters,
                x.SuggestedBehavior, x.Confidence,
                new[] { new EvidenceReference("PortfolioAssessment", $"{x.AssessmentId}:{x.AssessmentVersion}", x.Evidence ?? x.Summary, x.WhyItMatters) }
                    .Concat(events.Where(e => e.AffectedAssets.Any(a => a.Id == position.AssetId)).SelectMany(e => e.Evidence.Select(reference =>
                        new EvidenceReference("Document", reference.DocumentId.ToString(), reference.Reference, reference.Reference, e.OccurredAt))))
                    .Distinct().ToArray())).ToArray();
            var deviation = deviations.GetValueOrDefault(position.AssetClass);
            var candidate = new ContributionCandidate(CandidateIdentity.Known(position.AssetId), position.Ticker, position.AssetClass, position.CurrentValue, position.CurrentWeight,
                deviation?.CurrentPercentage ?? 0, deviation?.TargetPercentage ?? 0, requiresRebalance ? classBudgets.GetValueOrDefault(position.AssetClass) : amount,
                inputs.Any(x => x.AssetId == position.AssetId && x.Impact == CapitalDecisionImpact.Block), contexts,
                position.AssetType, position.Sector, position.RecentContributionAmount, position.RecentContributionCount,
                position.LastContributionAt, position.SectorWeightWithinClass, position.HasPosition,
                position.AllocationNeedScore, position.DiversificationScore, position.UnderweightScore,
                position.RecentContributionPenalty, position.SectorCategory, position.GeographicExposureCoverage,
                position.GeographicExposures, position.EconomicCurrencyExposureCoverage, position.CurrencyExposures,
                position.ListingCountryCode, position.TradingCurrency, position.FinancialSnapshot)
            {
                FundType = position.FundType,
                UnderlyingSectorExposure = position.UnderlyingSectorExposure,
                IsEstablishingPosition = position.IsEstablishingPosition
            };
            return candidate with
            {
                CandidateIntelligence = CandidateIntelligenceBuilder.Build(
                    candidate,
                    position.HasPosition ? portfolio.Positions.Single(x => x.AssetId == position.AssetId).Asset! : catalogCandidates.Single(x => x.Id == position.AssetId),
                    events, contexts)
            };
        }).ToArray();

        return new(intelligenceContextKey, baseline, positions, candidates);
    }

    private static decimal ConfidenceValue(IntelligenceConfidence confidence) => confidence switch
    {
        IntelligenceConfidence.High => 0.9m,
        IntelligenceConfidence.Medium => 0.7m,
        _ => 0.4m
    };

    private static string NormalizeSector(string? sector) => string.IsNullOrWhiteSpace(sector) ? string.Empty : sector.Trim().ToUpperInvariant();

    public static PersistedContributionGuidanceWorkItem BuildPersistableWorkItem(ContributionGuidance baseline, IReadOnlyList<GuidancePosition> positions, IReadOnlyList<ContributionCandidate> candidates)
    {
        const int physicalLimit = int.MaxValue;
        var full = new ContributionGuidanceWorkItem(baseline, positions, candidates);
        var originalChars = JsonSerializer.Serialize(full).Length;

        var projectedCandidates = candidates.Select(ProjectForR5).ToArray();
        var projected = new PersistedContributionGuidanceWorkItem(
            PersistedContributionBaseline.From(baseline), projectedCandidates.Select(PersistedContributionCandidate.From).ToArray());
        var projectedChars = JsonSerializer.Serialize(projected).Length;
        var selected = projected;
        var persistedChars = projectedChars;
        var reductionPercent = originalChars == 0 ? 0 : decimal.Round((originalChars - persistedChars) * 100m / originalChars, 2);
        selected = selected with
        {
            CandidatePayloadOriginalChars = originalChars,
            CandidatePayloadProjectedChars = projectedChars,
            CandidatePayloadPersistedChars = persistedChars,
            CandidatePayloadReductionPercent = reductionPercent,
            CandidatePayloadTruncatedEventCount = 0,
            CandidatePayloadTruncatedSummaryCount = 0
        };
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var serializedLength = JsonSerializer.Serialize(selected).Length;
            if (serializedLength == selected.CandidatePayloadPersistedChars) break;
            selected = selected with { CandidatePayloadPersistedChars = serializedLength };
            persistedChars = serializedLength;
        }
        persistedChars = JsonSerializer.Serialize(selected).Length;
        // Only guard the provider/runtime maximum; database size is no longer
        // used to decide whether decision context is removed.
        if (persistedChars >= physicalLimit)
            throw new InvalidOperationException($"O contexto de candidatos excede o limite físico de {physicalLimit} caracteres mesmo após a projeção e a compactação técnica.");
        return selected with { CandidatePayloadPersistedChars = persistedChars };
    }

    private static ContributionCandidate ProjectForR5(ContributionCandidate candidate) => candidate with
    {
        CurrentAssessments = Array.Empty<ContributionAssessmentContext>(),
        CandidateIntelligence = candidate.CandidateIntelligence is null ? null : CandidateIntelligenceBuilder.ProjectForR5(candidate.CandidateIntelligence)
    };

    private static decimal ScoreNeed(decimal target, decimal current) => Math.Clamp((target - current) * 5m, 0, 100);

}

public sealed record ContributionContext(string IntelligenceContextKey, ContributionGuidance Baseline, IReadOnlyList<GuidancePosition> Positions, IReadOnlyList<ContributionCandidate> Candidates);
