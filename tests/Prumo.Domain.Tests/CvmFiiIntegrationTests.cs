using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Prumo.Application.Contributions;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.DocumentSources;
using Prumo.Infrastructure.MarketData;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Domain.Tests;

public sealed class CvmFiiIntegrationTests
{
    [Fact]
    public async Task EnrichesIngestsLoadsAndResolvesOfficialFiiEvidence()
    {
        await using var db = new PrumoDbContext(new DbContextOptionsBuilder<PrumoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var portfolio = new Portfolio("Integration");
        var asset = new Asset("FIIA11", "FII Industrial do Brasil", AssetType.Fii, "Industrial Real Estate");
        db.AddRange(portfolio, asset, new PortfolioPosition(portfolio.Id, asset.Id, 1m, 100m, 100m));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var options = Options.Create(new CvmFnetOptions { CadastroFiiUrl = "https://cvm.test/cadastro.zip", InformeMensalFiiUrlTemplate = "https://cvm.test/mensal-{0}.zip" });
        using var client = new HttpClient(new CvmHandler(CreateRegistryZip(), CreateMonthlyZip()));
        var enrichment = new CvmFiiCnpjEnrichmentService(db, client, options, NullLogger<CvmFiiCnpjEnrichmentService>.Instance);
        var enriched = await enrichment.EnrichAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, enriched.EnrichedAssets);
        Assert.Equal("14217108000145", asset.Cnpj);

        var source = new CvmFiiMonthlyStructuredSource(client, options);
        var ingestion = new CvmFiiMonthlyIngestionService(db, source, NullLogger<CvmFiiMonthlyIngestionService>.Instance);
        var ingested = await ingestion.IngestAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, ingested.ObservationsInserted);

        await db.Entry(asset).Collection(x => x.CvmFiiMonthlyObservations).LoadAsync(TestContext.Current.CancellationToken);
        var candidate = new ContributionCandidate(CandidateIdentity.Known(asset.Id), asset.Ticker, AssetClass.Fii, 100m, 100m, 100m, 100m, 100m, false, [], AssetType.Fii, asset.Sector, 0, 0, null, 0, true, 0, 0, 0, 0, null, ExposureCoverage.Unavailable, [], ExposureCoverage.Unavailable, [], "BR", "BRL", null)
        { FundType = FundType.Brick };
        var intelligence = CandidateIntelligenceBuilder.Build(candidate, asset, [], []);
        var evidence = intelligence.MetricEvidence;

        Assert.Equal(MetricEvidenceSourceType.OfficialStructured, evidence.Single(x => x.Metric == "NetAssetValue").Canonical!.SourceType);
        Assert.Equal(new DateOnly(2026, 1, 1), evidence.Single(x => x.Metric == "NAVPerShare").Canonical!.ReferenceDate);
        Assert.Equal(500m, evidence.Single(x => x.Metric == "NetAssetValue").Canonical!.Value);
        Assert.Equal(12.5m, evidence.Single(x => x.Metric == "NAVPerShare").Canonical!.Value);
        Assert.Equal(25m, evidence.Single(x => x.Metric == "Cash").Canonical!.Value);
        Assert.Contains(intelligence.MetricEvidence, x => x.Metric == "NetAssetValue");
    }

    private static byte[] CreateRegistryZip() => CreateZip("registro_fundo_classe.csv", "CNPJ_Fundo_Classe;Denominacao_Social;Situacao\n14.217.108/0001-45;FII Industrial do Brasil Responsabilidade Limitada;ATIVO\n");
    private static byte[] CreateMonthlyZip() => CreateZip(
        "inf_mensal_fii_ativo_passivo_2026.csv", "CNPJ_Fundo_Classe;Data_Referencia;Versao;Disponibilidades\n14.217.108/0001-45;2026-01-01;1;25\n",
        "inf_mensal_fii_complemento_2026.csv", "CNPJ_Fundo_Classe;Data_Referencia;Versao;Patrimonio_Liquido;Valor_Patrimonial_Cotas\n14.217.108/0001-45;2026-01-01;1;500;12.5\n");

    private static byte[] CreateZip(params string[] values)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            for (var i = 0; i < values.Length; i += 2)
            using (var writer = new StreamWriter(archive.CreateEntry(values[i]).Open(), new UTF8Encoding(false)))
                writer.Write(values[i + 1]);
        return stream.ToArray();
    }

    private sealed class CvmHandler(byte[] registry, byte[] monthly) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.RequestUri!.AbsolutePath.Contains("cadastro", StringComparison.Ordinal) ? registry : monthly;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        }
    }
}
