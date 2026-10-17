using Microsoft.EntityFrameworkCore;
using Prumo.Application.Contributions;
using Prumo.Application.Documents;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Domain.Tests;

public sealed class DocumentFinancialQualityTests
{
    [Fact]
    public void Structured_extraction_produces_normalized_financial_facts()
    {
        var result = new AiAnalysisValidator().Validate(new AiAnalysisPayload("Informative", "FinancialResult", false, "NoAction", "resultado", "fato", null, ["AAA3"], .9m, [ ], [new("NetMargin", 12.5m, "2025-12-31", "Annual", null, "percent", "margem líquida de 12,5%", .92m)]), "{}");
        var fact = Assert.Single(result.FinancialFacts!);
        Assert.Equal(FinancialFactMetric.NetMargin, fact.Metric);
        Assert.Equal(.125m, fact.Value);
        Assert.Equal("ratio", fact.Unit);
    }

    [Fact]
    public void Document_financial_evidence_supports_quality_without_market_provider()
    {
        var asset = Asset("AAA3");
        var document = new Document(asset.Id, DocumentType.QuarterlyReport, DateTimeOffset.UtcNow, "CVM", "doc-1", "https://example.test/doc-1", "fingerprint-1");
        asset.DocumentFinancialFacts.Add(new DocumentFinancialFact(document.Id, asset.Id, FinancialFactMetric.NetIncome, new(2025, 12, 31), FinancialPeriodType.Annual, 100_000_000m, "BRL", "currency", "lucro líquido de R$ 100 milhões", .95m));
        asset.DocumentFinancialFacts.Add(new DocumentFinancialFact(document.Id, asset.Id, FinancialFactMetric.Equity, new(2025, 12, 31), FinancialPeriodType.Annual, 500_000_000m, "BRL", "currency", "patrimônio líquido de R$ 500 milhões", .95m));
        asset.DocumentFinancialFacts.Add(new DocumentFinancialFact(document.Id, asset.Id, FinancialFactMetric.Roe, new(2025, 12, 31), FinancialPeriodType.Annual, .18m, null, "ratio", "ROE de 18%", .95m));

        var snapshot = DocumentFinancialQualityResolver.Resolve(asset);
        var result = CandidateFinancialEvaluator.Evaluate(asset.Id, AssetType.Stock, snapshot);

        Assert.Equal(100_000_000m, snapshot!.NetIncome);
        Assert.Equal(.18m, snapshot.Roe);
        Assert.Equal("Acceptable", result.Quality.Category);
    }

    [Fact]
    public void Missing_metric_never_becomes_zero()
    {
        var payload = new AiFinancialFactPayload("Roe", null, "2025-12-31", "Annual", null, "percent", "", .9m);
        Assert.Null(DocumentFinancialFactNormalizer.Normalize(payload));
    }

    [Fact]
    public void Ebitda_percent_is_rejected_with_metric_unit_reason()
    {
        var result = DocumentFinancialFactNormalizer.NormalizeWithReason(new("EBITDA", 87.2m, "2026-06-30", "Quarterly", "percent", "percent", "margem EBITDA de 87,2%", .9m));

        Assert.False(result.IsAccepted);
        Assert.Null(result.Fact);
        Assert.Equal("InvalidUnitForMetric", result.RejectionReason);
    }

    [Fact]
    public void Monthly_period_is_rejected_with_period_reason()
    {
        var result = DocumentFinancialFactNormalizer.NormalizeWithReason(new("Equity", 100m, "2026-08-31", "Monthly", "BRL", "currency", "patrimônio", .9m));

        Assert.False(result.IsAccepted);
        Assert.Null(result.Fact);
        Assert.Equal("InvalidPeriodType", result.RejectionReason);
    }

    [Fact]
    public void Valid_fact_is_accepted_without_rejection()
    {
        var result = DocumentFinancialFactNormalizer.NormalizeWithReason(new("TotalDebt", 51_796_856m, "2026-08-31", "Quarterly", "BRL", "currency", "dívida", 1m));

        Assert.True(result.IsAccepted);
        Assert.NotNull(result.Fact);
        Assert.Null(result.RejectionReason);
    }

    [Theory]
    [InlineData("currency_billion", "10000000")]
    [InlineData("currency_billion", "-10000000")]
    [InlineData("currency_billion", "79228162514264337593543950335")]
    [InlineData("currency", "10000000000000000")]
    public void Monetary_fact_outside_database_range_is_rejected(string unit, string value)
    {
        var result = DocumentFinancialFactNormalizer.NormalizeWithReason(new("Revenue", decimal.Parse(value), "2026-08-31", "Quarterly", "BRL", unit, "receita", .9m));

        Assert.Equal("ValueOutOfRange", result.RejectionReason);
        Assert.Null(result.Fact);
    }

    [Theory]
    [InlineData("Equity", "1990000000", "PointInTime")]
    [InlineData("Revenue", "28900000000", "Annual")]
    public void Base_currency_facts_are_accepted_but_duplicate_billion_scale_is_rejected(string metric, string value, string periodType)
    {
        var payload = new AiFinancialFactPayload(metric, decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
            "2025-12-31", periodType, "USD", "currency", "reported amount", .9m);

        var accepted = DocumentFinancialFactNormalizer.NormalizeWithReason(payload);
        var rejected = DocumentFinancialFactNormalizer.NormalizeWithReason(payload with { Unit = "currency_billion" });

        Assert.True(accepted.IsAccepted);
        Assert.Equal(payload.Value, accepted.Fact!.Value);
        Assert.Equal("currency", accepted.Fact.Unit);
        Assert.Equal("ValueOutOfRange", rejected.RejectionReason);
        Assert.Null(rejected.Fact);
    }

    [Fact]
    public void Monetary_fact_uses_database_rounding_at_range_boundary()
    {
        var accepted = DocumentFinancialFactNormalizer.NormalizeWithReason(new("Revenue", 9_999_999_999_999_999.999999991m, "2026-08-31", "Quarterly", "BRL", "currency", "receita", .9m));
        var rejected = DocumentFinancialFactNormalizer.NormalizeWithReason(new("Revenue", 9_999_999_999_999_999.999999995m, "2026-08-31", "Quarterly", "BRL", "currency", "receita", .9m));

        Assert.True(accepted.IsAccepted);
        Assert.Equal("ValueOutOfRange", rejected.RejectionReason);
    }

    [Fact]
    public void Out_of_range_fact_does_not_discard_valid_fact_from_same_analysis()
    {
        var analysis = new AiAnalysisValidator().Validate(new AiAnalysisPayload(
            "Informative", "FinancialResult", false, "NoAction", "resumo", "impacto", null, ["AAA3"], .9m, [],
            [
                new("Revenue", 10_000_000m, "2026-08-31", "Quarterly", "BRL", "currency_billion", "receita incorreta", .9m),
                new("TotalDebt", 7_600_000_000m, "2026-08-31", "Quarterly", "BRL", "currency", "dívida", .9m)
            ]), "{}");

        Assert.Single(analysis.FinancialFacts!);
        Assert.Equal(7_600_000_000m, analysis.FinancialFacts![0].Value);
        Assert.Equal("ValueOutOfRange", Assert.Single(analysis.RejectedFinancialFacts!).Reason);
    }

    [Fact]
    public void Rejected_fact_does_not_discard_valid_facts_from_same_payload()
    {
        var analysis = new AiAnalysisValidator().Validate(new AiAnalysisPayload(
            "Informative", "FinancialResult", false, "NoAction", "resumo", "impacto", null, ["AAA3"], .9m, [],
            [
                new("TotalDebt", 100m, "2026-08-31", "Quarterly", "BRL", "currency", "dívida", .9m),
                new("EBITDA", 87.2m, "2026-06-30", "Quarterly", "percent", "percent", "margem", .8m),
                new("Equity", 200m, "2026-08-31", "Monthly", "BRL", "currency", "patrimônio", .8m)
            ]), "{}");

        Assert.Single(analysis.FinancialFacts!);
        Assert.Equal(2, analysis.RejectedFinancialFacts!.Count);
        Assert.Contains(analysis.RejectedFinancialFacts, x => x.Reason == "InvalidUnitForMetric");
        Assert.Contains(analysis.RejectedFinancialFacts, x => x.Reason == "InvalidPeriodType");
    }

    [Fact]
    public void Equivalent_percentage_representations_normalize_identically()
    {
        var percent = DocumentFinancialFactNormalizer.Normalize(new("Roe", 18, "2025-12-31", "Annual", null, "percent", "ROE", .9m));
        var ratio = DocumentFinancialFactNormalizer.Normalize(new("Roe", .18m, "2025-12-31", "Annual", null, "ratio", "ROE", .9m));
        Assert.Equal(percent!.Value, ratio!.Value);
    }

    [Fact]
    public void Monetary_scale_normalizes_deterministically()
    {
        var fact = DocumentFinancialFactNormalizer.Normalize(new("Revenue", 2.4m, "2025-12-31", "Annual", "BRL", "currency_billion", "receita", .9m));
        Assert.Equal(2_400_000_000m, fact!.Value);
    }

    [Fact]
    public void Quarterly_fact_does_not_replace_annual_fact_in_same_period()
    {
        var asset = Asset("AAA3"); var document = Document(asset.Id, "q");
        asset.DocumentFinancialFacts.Add(new DocumentFinancialFact(document.Id, asset.Id, FinancialFactMetric.Roe, new(2025, 12, 31), FinancialPeriodType.Annual, .18m, null, "ratio", "annual", .9m));
        asset.DocumentFinancialFacts.Add(new DocumentFinancialFact(document.Id, asset.Id, FinancialFactMetric.Roe, new(2025, 12, 31), FinancialPeriodType.Quarterly, .22m, null, "ratio", "quarterly", .9m));
        Assert.Equal(.18m, DocumentFinancialQualityResolver.Resolve(asset)!.Roe);
    }

    [Fact]
    public void Conflicting_facts_are_unavailable_not_averaged()
    {
        var asset = Asset("AAA3"); var first = Document(asset.Id, "a"); var second = Document(asset.Id, "b");
        asset.DocumentFinancialFacts.Add(new DocumentFinancialFact(first.Id, asset.Id, FinancialFactMetric.Roe, new(2025, 12, 31), FinancialPeriodType.Annual, .18m, null, "ratio", "one", .9m));
        asset.DocumentFinancialFacts.Add(new DocumentFinancialFact(second.Id, asset.Id, FinancialFactMetric.Roe, new(2025, 12, 31), FinancialPeriodType.Annual, .22m, null, "ratio", "two", .9m));
        Assert.Null(DocumentFinancialQualityResolver.Resolve(asset)!.Roe);
    }

    [Fact]
    public async Task Reprocessing_same_document_does_not_duplicate_financial_facts()
    {
        await using var db = new PrumoDbContext(new DbContextOptionsBuilder<PrumoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var asset = Asset("AAA3"); var document = Document(asset.Id, "a"); db.AddRange(asset, document);
        db.DocumentFinancialFacts.Add(new DocumentFinancialFact(document.Id, asset.Id, FinancialFactMetric.Roe, new(2025, 12, 31), FinancialPeriodType.Annual, .18m, null, "ratio", "ROE", .9m)); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        if (!await db.DocumentFinancialFacts.AnyAsync(x => x.DocumentId == document.Id && x.Metric == FinancialFactMetric.Roe && x.ReferencePeriod == new DateOnly(2025, 12, 31) && x.PeriodType == FinancialPeriodType.Annual, cancellationToken: TestContext.Current.CancellationToken)) db.DocumentFinancialFacts.Add(new DocumentFinancialFact(document.Id, asset.Id, FinancialFactMetric.Roe, new(2025, 12, 31), FinancialPeriodType.Annual, .18m, null, "ratio", "ROE", .9m));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Single(await db.DocumentFinancialFacts.ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Quality_source_is_neutral_for_equivalent_evidence()
    {
        var document = new CandidateFinancialSnapshot(new(2025, 12, 31), FinancialPeriodType.Annual, DateTimeOffset.UtcNow, FinancialDataSource.Derived, NetIncome: 100, Roe: .18m);
        var provider = new CandidateFinancialSnapshot(new(2025, 12, 31), FinancialPeriodType.Annual, DateTimeOffset.UtcNow, FinancialDataSource.Provider, NetIncome: 100, Roe: .18m);
        Assert.Equal(CandidateFinancialEvaluator.Evaluate(Guid.NewGuid(), AssetType.Stock, document).Quality.Category, CandidateFinancialEvaluator.Evaluate(Guid.NewGuid(), AssetType.Stock, provider).Quality.Category);
    }

    private static Asset Asset(string ticker) => new(ticker, ticker, AssetType.Stock, "Finance");
    private static Document Document(Guid assetId, string id) => new(assetId, DocumentType.QuarterlyReport, DateTimeOffset.UtcNow, "CVM", id, $"https://example.test/{id}", $"fingerprint-{id}");
}
