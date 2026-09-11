using System.IO.Compression;
using System.Text;
using Prumo.Application.Contributions;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.DocumentSources;

namespace Prumo.Domain.Tests;

public sealed class CvmFiiMonthlyStructuredIngestionTests
{
    [Fact]
    public void ParsesCvmMonthlyFieldsWithFundClassIdentityAndSemantics()
    {
        var records = CvmFiiMonthlyStructuredSource.ParseZip(CreateZip(
            ("inf_mensal_fii_ativo_passivo_2026.csv", "CNPJ_Fundo_Classe;Data_Referencia;Versao;Disponibilidades\n14.217.108/0001-45;2026-01-01;1;123.45\n"),
            ("inf_mensal_fii_complemento_2026.csv", "CNPJ_Fundo_Classe;Data_Referencia;Versao\n")),
            new HashSet<string> { "14217108000145" }, "https://cvm.test/data.zip");

        var record = Assert.Single(records);
        Assert.Equal("14217108000145", record.FundClassCnpj);
        Assert.Equal(new DateOnly(2026, 1, 1), record.ReferenceDate);
        Assert.Equal(123.45m, record.Cash);
        Assert.Null(record.NetAssetValue);
        Assert.Null(record.NavPerShare);
        Assert.Contains("cnpj=14217108000145", record.SourceReference);
    }

    [Fact]
    public void DoesNotConfuseNavWithNavPerShareAndMissingFieldsAreUnavailable()
    {
        var records = CvmFiiMonthlyStructuredSource.ParseZip(CreateZip(
            ("inf_mensal_fii_ativo_passivo_2026.csv", "CNPJ_Fundo_Classe;Data_Referencia;Versao\n"),
            ("inf_mensal_fii_complemento_2026.csv", "CNPJ_Fundo_Classe;Data_Referencia;Versao;Patrimonio_Liquido;Valor_Patrimonial_Cotas\n08.431.747/0001-06;2026-02-01;2;1000000;12.50\n")),
            new HashSet<string> { "08431747000106" }, "https://cvm.test/data.zip");
        var asset = new Asset("HGBS11", "Hedge Brasil Shopping", AssetType.Fii, "Shopping Centers");
        asset.CvmFiiMonthlyObservations.Add(new(asset.Id, records[0].FundClassCnpj, records[0].ReferenceDate, records[0].SourceVersion, records[0].Fingerprint, records[0].SourceReference, records[0].SchemaVersion, DateTimeOffset.UtcNow, records[0].NetAssetValue, records[0].NavPerShare, records[0].Cash));

        var evidence = MetricEvidenceResolver.Resolve(asset);
        Assert.Equal(1000000m, evidence.Metrics.Single(x => x.Metric == "NetAssetValue").Canonical!.Value);
        Assert.Equal("currency", evidence.Metrics.Single(x => x.Metric == "NetAssetValue").Canonical!.Unit);
        Assert.Equal(12.50m, evidence.Metrics.Single(x => x.Metric == "NAVPerShare").Canonical!.Value);
        Assert.DoesNotContain(evidence.Metrics, x => x.Metric == "DistributionPerShare");
        var coverage = MetricApplicabilityResolver.Dimension(CandidateIntelligenceDimension.IncomeDistributions, AssetType.Fii, FundType.Brick, evidence, DateTimeOffset.UtcNow);
        Assert.Equal(MetricApplicability.Unavailable, coverage.Applicability);
        Assert.Contains("DistributionPerShare", coverage.MissingMetrics);
    }

    [Fact]
    public void FiiMetricsDoNotApplyToStocksOrNonBrickFunds()
    {
        var empty = new MetricEvidenceSet(Array.Empty<MetricEvidenceResolution>());
        Assert.Equal(MetricApplicability.NotApplicable, MetricApplicabilityResolver.For("NetAssetValue", AssetType.Stock, FundType.Unknown));
        Assert.Equal(MetricApplicability.NotApplicable, MetricApplicabilityResolver.For("NetAssetValue", AssetType.Fii, FundType.Receivables));
        Assert.Equal(MetricApplicability.NotApplicable, MetricApplicabilityResolver.For("NetAssetValue", AssetType.Fii, FundType.FundOfFunds));
        Assert.Equal(MetricApplicability.NotApplicable, MetricApplicabilityResolver.Dimension(CandidateIntelligenceDimension.FinancialFundamental, AssetType.Fii, FundType.FundOfFunds, empty, DateTimeOffset.UtcNow).Applicability);
    }

    [Fact]
    public void StructuredEvidencePreservesOfficialProvenanceWithoutAiConfidence()
    {
        var asset = new Asset("FIIB11", "FII Industrial", AssetType.Fii, "Industrial Real Estate");
        var observation = new CvmFiiMonthlyObservation(asset.Id, "14217108000145", new(2026, 1, 1), 1, "abc123", "https://dados.cvm.gov.br/data.zip#cnpj=14217108000145", CvmFiiMonthlyStructuredSource.SchemaVersion, DateTimeOffset.UtcNow, 500m, 10m, 2m);
        asset.CvmFiiMonthlyObservations.Add(observation);

        var evidence = MetricEvidenceResolver.Resolve(asset).CanonicalEvidence.Single(x => x.Metric == "NetAssetValue");
        Assert.Equal(observation.SourceReference, evidence.SourceReference);
        Assert.Equal(observation.ObservedAt, evidence.ObservedAt);
        Assert.Null(evidence.Confidence);
    }

    [Fact]
    public void SameSourceRowIsIdempotentAndChangedRepresentationGetsNewFingerprint()
    {
        var firstZip = CreateZip(
            ("inf_mensal_fii_ativo_passivo_2026.csv", "CNPJ_Fundo_Classe;Data_Referencia;Versao;Disponibilidades\n14.217.108/0001-45;2026-01-01;1;2\n"),
            ("inf_mensal_fii_complemento_2026.csv", "CNPJ_Fundo_Classe;Data_Referencia;Versao\n"));
        var secondZip = CreateZip(
            ("inf_mensal_fii_ativo_passivo_2026.csv", "CNPJ_Fundo_Classe;Data_Referencia;Versao;Disponibilidades\n14.217.108/0001-45;2026-01-01;2;3\n"),
            ("inf_mensal_fii_complemento_2026.csv", "CNPJ_Fundo_Classe;Data_Referencia;Versao\n"));

        var first = Assert.Single(CvmFiiMonthlyStructuredSource.ParseZip(firstZip, new HashSet<string> { "14217108000145" }, "https://cvm.test/data.zip"));
        var same = Assert.Single(CvmFiiMonthlyStructuredSource.ParseZip(firstZip, new HashSet<string> { "14217108000145" }, "https://cvm.test/data.zip"));
        var restated = Assert.Single(CvmFiiMonthlyStructuredSource.ParseZip(secondZip, new HashSet<string> { "14217108000145" }, "https://cvm.test/data.zip"));

        Assert.Equal(first.Fingerprint, same.Fingerprint);
        Assert.NotEqual(first.Fingerprint, restated.Fingerprint);
        Assert.Equal(1, first.SourceVersion);
        Assert.Equal(2, restated.SourceVersion);
    }

    [Fact]
    public void LatestOfficialRestatementIsCanonicalAndPreviousVersionRemainsAlternative()
    {
        var asset = new Asset("FIIB11", "FII Industrial", AssetType.Fii, "Industrial Real Estate");
        asset.CvmFiiMonthlyObservations.Add(new(asset.Id, "14217108000145", new(2026, 1, 1), 1, "old", "https://cvm.test/data.zip#cnpj=14217108000145&version=1", CvmFiiMonthlyStructuredSource.SchemaVersion, DateTimeOffset.UtcNow.AddDays(-1), 100m, 10m, 2m));
        asset.CvmFiiMonthlyObservations.Add(new(asset.Id, "14217108000145", new(2026, 1, 1), 2, "new", "https://cvm.test/data.zip#cnpj=14217108000145&version=2", CvmFiiMonthlyStructuredSource.SchemaVersion, DateTimeOffset.UtcNow, 110m, 11m, 2m));

        var resolution = MetricEvidenceResolver.Resolve(asset).Metrics.Single(x => x.Metric == "NetAssetValue");
        Assert.Equal(MetricEvidenceResolutionStatus.Resolved, resolution.Status);
        Assert.Equal(110m, resolution.Canonical!.Value);
        Assert.Contains(resolution.Alternatives, x => x.Value == 100m);
    }

    private static byte[] CreateZip(params (string FileName, string Content)[] files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var file in files)
            using (var writer = new StreamWriter(archive.CreateEntry(file.FileName).Open(), new UTF8Encoding(false)))
                writer.Write(file.Content);
        return stream.ToArray();
    }
}
