using Prumo.Application.Contributions;
using Prumo.Application.Explainability;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class MetricEvidenceV2A1Tests
{
    [Fact]
    public void Metrics_keep_individual_dates_sources_and_all_snapshot_fields()
    {
        var asset = new Asset("NORM3", "Normalização", AssetType.Stock, "Industrial");
        asset.FundamentalSnapshots.Add(new(asset.Id, new(2025, 12, 31), FinancialPeriodType.Annual, FinancialDataSource.Provider, "fund-a", DateTimeOffset.UtcNow.AddDays(-2), revenue: 10, ebitda: 3, roic: .2m, netMargin: .1m));
        asset.ValuationSnapshots.Add(new(asset.Id, new(2026, 9, 10), FinancialDataSource.Manual, "market-b", DateTimeOffset.UtcNow, priceEarnings: 8));

        var result = MetricEvidenceResolver.Resolve(asset);

        Assert.Equal(new DateOnly(2025, 12, 31), result.Metrics.Single(x => x.Metric == "Revenue").Canonical!.ReferenceDate);
        Assert.Equal("fund-a", result.Metrics.Single(x => x.Metric == "Revenue").Canonical!.SourceReference);
        Assert.Equal(new DateOnly(2026, 9, 10), result.Metrics.Single(x => x.Metric == "PriceEarnings").Canonical!.ReferenceDate);
        Assert.Equal("market-b", result.Metrics.Single(x => x.Metric == "PriceEarnings").Canonical!.SourceReference);
        Assert.Contains(result.CanonicalEvidence, x => x.Metric == "Ebitda");
        Assert.Contains(result.CanonicalEvidence, x => x.Metric == "Roic");
        Assert.Contains(result.CanonicalEvidence, x => x.Metric == "NetMargin");
    }

    [Fact]
    public void Document_units_currency_and_conflicts_are_preserved_explicitly()
    {
        var asset = new Asset("NORM4", "Normalização", AssetType.Stock, "Industrial");
        var first = new Document(asset.Id, DocumentType.QuarterlyReport, DateTimeOffset.UtcNow, "CVM", "a", "https://example.test/a", "fingerprint-a");
        var second = new Document(asset.Id, DocumentType.QuarterlyReport, DateTimeOffset.UtcNow, "CVM", "b", "https://example.test/b", "fingerprint-b");
        asset.DocumentFinancialFacts.Add(new(first.Id, asset.Id, FinancialFactMetric.Revenue, new(2025, 12, 31), FinancialPeriodType.Annual, 100, "BRL", "currency", "one", .91m));
        asset.DocumentFinancialFacts.Add(new(second.Id, asset.Id, FinancialFactMetric.Revenue, new(2025, 12, 31), FinancialPeriodType.Annual, 110, "USD", "currency", "two", .92m));

        var resolution = MetricEvidenceResolver.Resolve(asset).Metrics.Single();

        Assert.Equal(MetricEvidenceResolutionStatus.Conflict, resolution.Status);
        Assert.Equal(2, resolution.Alternatives.Count + 1);
        Assert.Contains(new[] { resolution.Canonical! }.Concat(resolution.Alternatives), x => x.Currency == "BRL");
        Assert.Contains(new[] { resolution.Canonical! }.Concat(resolution.Alternatives), x => x.Currency == "USD");
        Assert.Equal("currency", resolution.Canonical!.Unit);
    }

    [Fact]
    public void Fii_stock_only_metrics_are_not_applicable_not_unavailable()
    {
        var asset = new Asset("FUND11", "Fundo", AssetType.Fii, "Brick");
        var candidate = new ContributionCandidate(CandidateIdentity.Known(asset.Id), asset.Ticker, asset.AssetClass, 100, 10, 100, 100, 100, false, Array.Empty<ContributionAssessmentContext>(), AssetType.Fii);

        var intelligence = CandidateIntelligenceBuilder.Build(candidate, asset, Array.Empty<InvestmentEvent>(), Array.Empty<ContributionAssessmentContext>());

        Assert.Equal(MetricApplicability.Unavailable, intelligence.DimensionalCoverage.Single(x => x.Dimension == CandidateIntelligenceDimension.FinancialFundamental).Applicability);
    }

    [Fact]
    public void Events_and_assessments_have_independent_coverage()
    {
        var asset = new Asset("EVT4", "Evento", AssetType.Stock, "Industrial");
        var investmentEvent = new InvestmentEvent(Guid.NewGuid(), InvestmentEventType.Other, DateTimeOffset.UtcNow, "Evento", new[] { "Fato" });
        investmentEvent.AffectedAssets.Add(asset);
        var assessment = new ContributionAssessmentContext("Medium", "Monitor", "None", "Resumo", "Motivo", "Acompanhar", .8m, Array.Empty<EvidenceReference>());
        var candidate = new ContributionCandidate(CandidateIdentity.Known(asset.Id), asset.Ticker, asset.AssetClass, 100, 10, 100, 100, 100, false, Array.Empty<ContributionAssessmentContext>(), AssetType.Stock);

        var intelligence = CandidateIntelligenceBuilder.Build(candidate, asset, new[] { investmentEvent }, new[] { assessment });

        Assert.Equal(MetricApplicability.Available, intelligence.DimensionalCoverage.Single(x => x.Dimension == CandidateIntelligenceDimension.Events).Applicability);
        Assert.Equal(MetricApplicability.Available, intelligence.DimensionalCoverage.Single(x => x.Dimension == CandidateIntelligenceDimension.Assessments).Applicability);
        Assert.Equal(MetricApplicability.Unavailable, intelligence.DimensionalCoverage.Single(x => x.Dimension == CandidateIntelligenceDimension.FinancialFundamental).Applicability);
    }

    [Fact]
    public void Freshness_is_evaluated_by_metric_family()
    {
        var asset = new Asset("FRESH3", "Freshness", AssetType.Stock, "Industrial");
        asset.FundamentalSnapshots.Add(new(asset.Id, new(2025, 12, 31), FinancialPeriodType.Annual, FinancialDataSource.Provider, "annual", DateTimeOffset.UtcNow.AddDays(-100), netIncome: 10));
        asset.ValuationSnapshots.Add(new(asset.Id, DateOnly.FromDateTime(DateTime.UtcNow.Date), FinancialDataSource.Provider, "valuation", DateTimeOffset.UtcNow.AddDays(-100), priceEarnings: 8));
        var candidate = new ContributionCandidate(CandidateIdentity.Known(asset.Id), asset.Ticker, asset.AssetClass, 100, 10, 100, 100, 100, false, Array.Empty<ContributionAssessmentContext>(), AssetType.Stock);

        var intelligence = CandidateIntelligenceBuilder.Build(candidate, asset, Array.Empty<InvestmentEvent>(), Array.Empty<ContributionAssessmentContext>());

        Assert.Equal(MetricApplicability.Available, intelligence.Freshness!.ByMetric["NetIncome"]);
        Assert.Equal(MetricApplicability.Stale, intelligence.Freshness.ByMetric["PriceEarnings"]);
    }
}
