using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Prumo.Application.Documents;
using Prumo.Application.Intelligence;
using Prumo.Application.PortfolioImports;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Worker;
using Npgsql;

namespace Prumo.Domain.Tests;

public sealed class DocumentAnalysisProcessorTests
{
    [RelationalFact]
    public async Task Concurrent_workers_defer_then_reuse_identical_text_with_different_file_hashes()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = $"prumo_document_dedup_{Guid.NewGuid():N}";
        var adminSettings = new NpgsqlConnectionStringBuilder("Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=dev123");
        await using var admin = new NpgsqlConnection(adminSettings.ConnectionString);
        await admin.OpenAsync(ct);
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin)) await create.ExecuteNonQueryAsync(ct);
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(adminSettings.ConnectionString) { Database = database }.ConnectionString;
            var options = new DbContextOptionsBuilder<PrumoDbContext>().UseNpgsql(connection).Options;
            await using var first = new PrumoDbContext(options);
            await first.Database.MigrateAsync(ct);
            Assert.False(first.Database.HasPendingModelChanges());
            var seeded = await SeedDocumentScenarioAsync(first);
            var duplicate = new Document(seeded.Document.AssetId, DocumentType.Other, DateTimeOffset.UtcNow,
                "CVM", "duplicate", "https://example.test/duplicate", "duplicate");
            duplicate.MarkDownloaded("duplicate.pdf", "different-file-hash");
            var job = new ProcessingJob(duplicate.Id, ProcessingJobType.AnalyzeDocument, DateTimeOffset.UtcNow);
            job.Start(DateTimeOffset.UtcNow);
            first.AddRange(duplicate, job);
            await first.SaveChangesAsync(ct);
            await using var second = new PrumoDbContext(options);
            var secondJob = await second.ProcessingJobs.SingleAsync(item => item.Id == job.Id, ct);
            var ai = new BlockingAiAnalysisService();
            var running = CreateProcessor(first, new FakeFileStorage(PdfTestData.CreatePdfStream(LongValidText())),
                new FakePdfTextExtractor(), ai, deletePdfAfterAnalysis: true).ProcessAsync(seeded.Job, ct);
            var processor = CreateProcessor(second, new FakeFileStorage(PdfTestData.CreatePdfStream(LongValidText())),
                new FakePdfTextExtractor(), new ThrowingAiAnalysisService(new Exception("Não deve chamar IA")), deletePdfAfterAnalysis: true);
            try
            {
                await ai.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
                await processor.ProcessAsync(secondJob, ct);
                Assert.Equal(ProcessingJobStatus.Queued, secondJob.Status);
                Assert.Equal(0, secondJob.Attempts);
            }
            finally
            {
                ai.Release.TrySetResult();
                await running;
            }
            secondJob.Start(DateTimeOffset.UtcNow);
            await processor.ProcessAsync(secondJob, ct);
            Assert.Equal(ProcessingJobStatus.Completed, secondJob.Status);
            Assert.Equal(seeded.Document.Id, (await second.Documents.SingleAsync(item => item.Id == duplicate.Id, ct)).DuplicateOfDocumentId);
            Assert.Single(await second.DocumentAnalyses.ToListAsync(ct));
            Assert.Equal(1, ai.Calls);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private sealed class BlockingAiAnalysisService : IAiAnalysisService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public string Model => "fake-model";
        public string PromptVersion => "document-analysis-v1";
        public async Task<AiAnalysisResult> AnalyzeAsync(string documentText, PortfolioAnalysisContext context, CancellationToken cancellationToken = default)
        {
            Calls++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await new FakeAiAnalysisService().AnalyzeAsync(documentText, context, cancellationToken);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Text_comparison_ignores_only_whitespace_even_after_pdf_deletion(bool sameText)
    {
        await using var db = CreateDbContext();
        var seeded = await SeedDocumentScenarioAsync(db);
        await CreateProcessor(db, new FakeFileStorage(PdfTestData.CreatePdfStream(LongValidText())),
            new FakePdfTextExtractor(), new FakeAiAnalysisService(), deletePdfAfterAnalysis: true)
            .ProcessAsync(seeded.Job, TestContext.Current.CancellationToken);
        Assert.NotNull(seeded.Document.TextHash);
        Assert.Null(seeded.Document.FilePath);
        db.ChangeTracker.Clear();

        var duplicate = new Document(seeded.Document.AssetId, DocumentType.Other, DateTimeOffset.UtcNow,
            "CVM", "text-copy", "https://example.test/text-copy", "text-copy");
        duplicate.MarkDownloaded("different-layout.pdf", "different-file-hash");
        var job = new ProcessingJob(duplicate.Id, ProcessingJobType.AnalyzeDocument, DateTimeOffset.UtcNow);
        job.Start(DateTimeOffset.UtcNow);
        db.AddRange(duplicate, job);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var text = sameText ? LongValidText().Replace(" ", " \r\n\t ") : LongValidText().Replace("evento", "outro evento");
        IAiAnalysisService ai = sameText ? new ThrowingAiAnalysisService(new Exception("Não deve chamar IA")) : new FakeAiAnalysisService();
        await CreateProcessor(db, new FakeFileStorage(PdfTestData.CreatePdfStream(text)),
            new FakePdfTextExtractor(new PdfTextExtractionResult(true, text, null)), ai, deletePdfAfterAnalysis: true)
            .ProcessAsync(job, TestContext.Current.CancellationToken);

        Assert.Equal(sameText ? 1 : 2, await db.DocumentAnalyses.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(sameText ? seeded.Document.Id : (Guid?)null, duplicate.DuplicateOfDocumentId);
        Assert.Equal(ProcessingJobStatus.Completed, job.Status);
        Assert.Null(duplicate.FilePath);
    }

    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(true, -4, false)]
    [InlineData(false, 0, false)]
    public async Task Waits_three_minutes_after_latest_download_only_for_same_asset(bool sameAsset, int minutesAgo, bool deferred)
    {
        await using var db = CreateDbContext();
        var seeded = await SeedDocumentScenarioAsync(db);
        var assetId = seeded.Document.AssetId;
        if (!sameAsset)
        {
            var otherAsset = new Asset("TEST11", "Outro ativo", AssetType.Fii, "Real Estate");
            db.Assets.Add(otherAsset);
            assetId = otherAsset.Id;
        }
        var otherDocument = new Document(assetId, DocumentType.Other, DateTimeOffset.UtcNow,
            "CVM", "downloaded", "https://example.test/downloaded", "downloaded");
        var completedAt = DateTimeOffset.UtcNow.AddMinutes(minutesAgo);
        var download = new ProcessingJob(otherDocument.Id, ProcessingJobType.DownloadDocument, completedAt);
        download.Start(completedAt);
        download.Complete(completedAt);
        db.AddRange(otherDocument, download);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await CreateProcessor(db, new FakeFileStorage(PdfTestData.CreatePdfStream(LongValidText())),
            new FakePdfTextExtractor(), new FakeAiAnalysisService()).ProcessAsync(seeded.Job, TestContext.Current.CancellationToken);

        Assert.Equal(deferred ? ProcessingJobStatus.Queued : ProcessingJobStatus.Completed, seeded.Job.Status);
        Assert.Equal(deferred ? 0 : 1, seeded.Job.Attempts);
        Assert.Equal(deferred ? 0 : 1, await db.DocumentAnalyses.CountAsync(TestContext.Current.CancellationToken));
        if (deferred) Assert.Equal(completedAt.AddMinutes(3), seeded.Job.AvailableAt);
    }

    [Fact]
    public async Task Mesmo_conteudo_com_quatro_ids_gera_uma_analise_mesmo_apos_remover_pdf()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedDocumentScenarioAsync(db);
        await CreateProcessor(db, new FakeFileStorage(PdfTestData.CreatePdfStream(LongValidText())),
            new FakePdfTextExtractor(), new FakeAiAnalysisService(), deletePdfAfterAnalysis: true)
            .ProcessAsync(seeded.Job, CancellationToken.None);
        Assert.Null(seeded.Document.FilePath);
        Assert.Equal("abc123", seeded.Document.FileHash);
        var eventCount = await db.InvestmentEvents.CountAsync(cancellationToken: TestContext.Current.CancellationToken);

        for (var i = 2; i <= 4; i++)
        {
            var duplicate = new Document(seeded.Document.AssetId, DocumentType.Other,
                DateTimeOffset.UtcNow.AddDays(i), "CVM", $"doc-{i}", $"https://example.test/{i}", $"fingerprint-{i}");
            duplicate.MarkDownloaded($"copy-{i}.pdf", "abc123");
            var job = new ProcessingJob(duplicate.Id, ProcessingJobType.AnalyzeDocument, DateTimeOffset.UtcNow);
            job.Start(DateTimeOffset.UtcNow);
            db.AddRange(duplicate, job);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            await CreateProcessor(db, new FakeFileStorage(PdfTestData.CreatePdfStream(LongValidText())),
                new FakePdfTextExtractor(), new ThrowingAiAnalysisService(new Exception("Não deve chamar IA")),
                deletePdfAfterAnalysis: true).ProcessAsync(job, CancellationToken.None);

            Assert.Equal(DocumentProcessingStatus.Ignored, duplicate.ProcessingStatus);
            Assert.Contains(seeded.Document.Id.ToString(), duplicate.IgnoredReason);
            Assert.Equal(ProcessingJobStatus.Completed, job.Status);
            Assert.Null(duplicate.FilePath);
            Assert.Equal("abc123", duplicate.FileHash);
        }

        Assert.Single(db.DocumentAnalyses);
        Assert.Equal(eventCount, await db.InvestmentEvents.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true, "abc123")]
    [InlineData(false, "different-content")]
    public async Task Outro_ativo_ou_conteudo_diferente_tem_analise_propria(bool otherAsset, string hash)
    {
        await using var db = CreateDbContext();
        var seeded = await SeedDocumentScenarioAsync(db);
        await CreateProcessor(db, new FakeFileStorage(PdfTestData.CreatePdfStream(LongValidText())),
            new FakePdfTextExtractor(), new FakeAiAnalysisService()).ProcessAsync(seeded.Job, CancellationToken.None);
        var asset = otherAsset ? new Asset("TEST11", "Outro ativo", AssetType.Fii, "Real Estate") : await db.Assets.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        if (otherAsset) db.Assets.Add(asset);
        var document = new Document(asset.Id, DocumentType.RelevantFact, DateTimeOffset.UtcNow,
            "CVM", "another", "https://example.test/another", "another");
        document.MarkDownloaded("another.pdf", hash);
        var job = new ProcessingJob(document.Id, ProcessingJobType.AnalyzeDocument, DateTimeOffset.UtcNow);
        job.Start(DateTimeOffset.UtcNow);
        db.AddRange(document, job);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await CreateProcessor(db, new FakeFileStorage(PdfTestData.CreatePdfStream(LongValidText())),
            new FakePdfTextExtractor(new PdfTextExtractionResult(true, LongValidText() + " Informação adicional.", null)), new FakeAiAnalysisService()).ProcessAsync(job, CancellationToken.None);
        Assert.Equal(2, await db.DocumentAnalyses.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(DocumentProcessingStatus.Processed, document.ProcessingStatus);
    }

    [Fact]
    public async Task Copia_em_erro_sem_analise_nao_impede_processamento()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedDocumentScenarioAsync(db);
        var failed = new Document(seeded.Document.AssetId, DocumentType.Other, DateTimeOffset.UtcNow,
            "CVM", "failed", "https://example.test/failed", "failed");
        failed.MarkDownloaded("failed.pdf", "abc123");
        failed.MarkError();
        db.Documents.Add(failed);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await CreateProcessor(db, new FakeFileStorage(PdfTestData.CreatePdfStream(LongValidText())),
            new FakePdfTextExtractor(), new FakeAiAnalysisService()).ProcessAsync(seeded.Job, CancellationToken.None);

        Assert.Equal(seeded.Document.Id, (await db.DocumentAnalyses.SingleAsync(cancellationToken: TestContext.Current.CancellationToken)).DocumentId);
        Assert.Equal(DocumentProcessingStatus.Processed, seeded.Document.ProcessingStatus);
    }

    [Fact]
    public async Task Documento_valido_gera_analise_persistida()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDocumentScenarioAsync(dbContext);
        var processor = CreateProcessor(dbContext, new FakeFileStorage(PdfTestData.CreatePdfStream(LongValidText())), new FakePdfTextExtractor(), new FakeAiAnalysisService());

        await processor.ProcessAsync(seeded.Job, CancellationToken.None);

        var analysis = await dbContext.DocumentAnalyses.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        var document = await dbContext.Documents.SingleAsync(item => item.Id == seeded.Document.Id, cancellationToken: TestContext.Current.CancellationToken);
        var position = await dbContext.PortfolioPositions.SingleAsync(item => item.Id == seeded.Position.Id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(DocumentProcessingStatus.Processed, document.ProcessingStatus);
        Assert.Equal(ProcessingJobStatus.Completed, seeded.Job.Status);
        Assert.Equal("fake-model", analysis.Model);
        Assert.Equal("document-analysis-v1", analysis.PromptVersion);
        Assert.Equal(AnalysisImportance.Critical, analysis.Importance);
        Assert.Equal(AnalysisClassification.Liquidation, analysis.Classification);
        Assert.Equal(210m, position.Quantity);
        Assert.Equal(30.17m, position.AveragePrice);
        Assert.Equal(6335.70m, position.CurrentValue!.Value);
        Assert.True(position.PortfolioWeight > 0);
    }

    [Fact]
    public async Task PDF_e_fechado_antes_de_ser_liberado()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDocumentScenarioAsync(dbContext);
        var storage = new FakeFileStorage(PdfTestData.CreatePdfStream(LongValidText()));
        var processor = CreateProcessor(dbContext, storage, new FakePdfTextExtractor(), new FakeAiAnalysisService(), deletePdfAfterAnalysis: true);

        await processor.ProcessAsync(seeded.Job, CancellationToken.None);

        Assert.True(storage.DeleteWasCalledAfterStreamClosed);
    }

    [Fact]
    public async Task Falha_de_extracao_produz_erro()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDocumentScenarioAsync(dbContext);
        var processor = CreateProcessor(dbContext, new FakeFileStorage(PdfTestData.CreatePdfStream("nao utilizado")), new FakePdfTextExtractor(new PdfTextExtractionResult(false, string.Empty, "Falha ao extrair texto: arquivo corrompido.")), new FakeAiAnalysisService());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(seeded.Job, CancellationToken.None));

        Assert.Contains("Falha ao extrair texto", exception.Message);
        Assert.Empty(dbContext.DocumentAnalyses);
    }

    [Fact]
    public async Task Falha_do_provider_de_ia_repassa_excecao_para_retry()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDocumentScenarioAsync(dbContext);
        var processor = CreateProcessor(dbContext, new FakeFileStorage(PdfTestData.CreatePdfStream(LongValidText())), new FakePdfTextExtractor(), new ThrowingAiAnalysisService(new HttpRequestException("provider indisponivel")));

        await Assert.ThrowsAsync<HttpRequestException>(() => processor.ProcessAsync(seeded.Job, CancellationToken.None));
        Assert.Empty(dbContext.DocumentAnalyses);
    }

    [Fact]
    public async Task Timeout_do_provider_repassa_excecao_para_retry()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDocumentScenarioAsync(dbContext);
        var processor = CreateProcessor(dbContext, new FakeFileStorage(PdfTestData.CreatePdfStream(LongValidText())), new FakePdfTextExtractor(), new ThrowingAiAnalysisService(new TaskCanceledException("timeout")));

        await Assert.ThrowsAsync<TaskCanceledException>(() => processor.ProcessAsync(seeded.Job, CancellationToken.None));
        Assert.Empty(dbContext.DocumentAnalyses);
    }

    [Fact]
    public async Task Resposta_invalida_da_ia_nao_persistida()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDocumentScenarioAsync(dbContext);
        var processor = CreateProcessor(dbContext, new FakeFileStorage(PdfTestData.CreatePdfStream(LongValidText())), new FakePdfTextExtractor(), new ThrowingAiAnalysisService(new InvalidOperationException("Summary ausente.")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(seeded.Job, CancellationToken.None));
        Assert.Empty(dbContext.DocumentAnalyses);
    }

    [Fact]
    public async Task Analise_existente_para_mesmo_job_eh_idempotente()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDocumentScenarioAsync(dbContext);
        dbContext.DocumentAnalyses.Add(new DocumentAnalysis(
            seeded.Job.Id,
            seeded.Document.Id,
            "fake-model",
            "document-analysis-v1",
            AnalysisImportance.Relevant,
            AnalysisClassification.CorporateAction,
            true,
            SuggestedAction.Monitor,
            "Resumo existente",
            "Impacto existente",
            "Impacto na carteira existente",
            new[] { "XPIN11" },
            0.7m,
            "{}"));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var processor = CreateProcessor(dbContext, new FakeFileStorage(PdfTestData.CreatePdfStream(LongValidText())), new FakePdfTextExtractor(), new FakeAiAnalysisService());

        await processor.ProcessAsync(seeded.Job, CancellationToken.None);

        Assert.Equal(1, await dbContext.DocumentAnalyses.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ProcessingJobStatus.Completed, seeded.Job.Status);
    }

    [Fact]
    public async Task Analise_global_nao_constroi_contexto_de_carteira_e_cria_assessment_por_carteira()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDocumentScenarioAsync(dbContext);
        var portfolio = await dbContext.Portfolios.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        for (var index = 1; index <= 5; index++)
        {
            var asset = new Asset($"TEST{index}11", $"Teste {index}", AssetType.Fii, "Diversos");
            dbContext.Assets.Add(asset);
            dbContext.PortfolioPositions.Add(new PortfolioPosition(portfolio.Id, asset.Id, 1m, 100m, 100m));
        }
        var secondPortfolio = new Portfolio("Outra carteira");
        dbContext.Portfolios.Add(secondPortfolio);
        dbContext.PortfolioPositions.Add(new PortfolioPosition(secondPortfolio.Id, seeded.Position.AssetId, 10m, 30.17m, 30.17m));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var ai = new CapturingAiAnalysisService();
        var processor = CreateProcessor(dbContext, new FakeFileStorage(PdfTestData.CreatePdfStream(LongValidText())), new FakePdfTextExtractor(), ai);
        await processor.ProcessAsync(seeded.Job, CancellationToken.None);

        Assert.NotEqual(portfolio.Id, ai.Context!.PortfolioId);
        Assert.Empty(ai.Context.PortfolioName);
        Assert.Equal(0, ai.Context.TotalPortfolioValue);
        Assert.Equal(0, ai.Context.PositionCount);
        Assert.Equal(0m, ai.Context.Quantity);
        Assert.Null(ai.Context.CurrentValue);
        Assert.Empty(ai.Context.TopPositions);
        Assert.Equal(0, ai.Context.LargestPositionWeight);
        Assert.Equal(0, ai.Context.TopThreePositionWeight);
        Assert.Equal(1, await dbContext.DocumentAnalyses.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(2, await dbContext.PortfolioAssessments.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Remove_duplicatas_de_assessments_pendentes_antes_de_persistir()
    {
        await using var dbContext = CreateDbContext();
        var portfolioId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var snapshotKey = "snapshot-atual";
        var targetKey = "target-atual";
        var version = "portfolio-impact-v1";

        dbContext.PortfolioAssessments.AddRange(
            CreateAssessment(portfolioId, eventId, snapshotKey, targetKey, version),
            CreateAssessment(portfolioId, eventId, snapshotKey, targetKey, version));

        DocumentAnalysisProcessor.RemoveDuplicatePendingAssessments(dbContext);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, await dbContext.PortfolioAssessments.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    private static PortfolioAssessment CreateAssessment(Guid portfolioId, Guid eventId, string snapshotKey, string targetKey, string version)
        => new(portfolioId, eventId, AttentionPriority.Medium, ActionRequirement.Monitor, CapitalDecisionImpact.Caution, AssessmentStatus.Detected, AssessmentUserState.Unread, "Resumo", "Motivo", "Monitorar", IntelligenceConfidence.Medium, targetKey, version, snapshotKey, 1);

    private static DocumentAnalysisProcessor CreateProcessor(PrumoDbContext dbContext, IFileStorage fileStorage, IPdfTextExtractor extractor, IAiAnalysisService aiAnalysisService, bool deletePdfAfterAnalysis = false)
    {
        return new DocumentAnalysisProcessor(
            dbContext,
            fileStorage,
            extractor,
            new TextQualityValidator(new TextQualityOptions { MinimumCharacters = 20, MinimumPrintableRatio = 0.7m }),
            aiAnalysisService,
            new PortfolioImpactAssessmentService(),
            NullLogger<DocumentAnalysisProcessor>.Instance,
            Options.Create(new DocumentRetentionOptions { DeletePdfAfterAnalysis = deletePdfAfterAnalysis }));
    }

    private sealed class CapturingAiAnalysisService : IAiAnalysisService
    {
        public PortfolioAnalysisContext? Context { get; private set; }
        public string Model => "test-model";
        public string PromptVersion => "document-analysis-v1";

        public Task<AiAnalysisResult> AnalyzeAsync(string documentText, PortfolioAnalysisContext context, CancellationToken cancellationToken = default)
        {
            Context = context;
            return Task.FromResult(new AiAnalysisResult(AnalysisImportance.Critical, AnalysisClassification.Other, false, SuggestedAction.NoAction, "Resumo", "Impacto", "Impacto na carteira", new[] { context.Ticker }, 1m, "{}"));
        }
    }

    private static PrumoDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PrumoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new PrumoDbContext(options);
    }

    private static async Task<(Document Document, ProcessingJob Job, PortfolioPosition Position)> SeedDocumentScenarioAsync(PrumoDbContext dbContext)
    {
        var asset = new Asset("XPIN11", "XP Industrial", AssetType.Fii, "Real Estate");
        var portfolio = new Portfolio("Minha carteira");
        var position = new PortfolioPosition(portfolio.Id, asset.Id, 210m, 30.17m, 30.17m);
        position.SetPortfolioWeight(position.CurrentValue ?? 0m);
        var document = new Document(asset.Id, DocumentType.RelevantFact, DateTimeOffset.Parse("2026-08-27T12:00:00Z"), "CVM", "doc-1", "https://example.test/documento", "fingerprint", "https://example.test/download.pdf");
        document.MarkDownloaded("D:\\storage\\documents\\xpin11.pdf", "abc123");
        var job = new ProcessingJob(document.Id, ProcessingJobType.AnalyzeDocument, DateTimeOffset.UtcNow);
        job.Start(DateTimeOffset.UtcNow);

        dbContext.Assets.Add(asset);
        dbContext.Portfolios.Add(portfolio);
        dbContext.PortfolioPositions.Add(position);
        dbContext.Documents.Add(document);
        dbContext.ProcessingJobs.Add(job);
        await dbContext.SaveChangesAsync();

        return (document, job, position);
    }

    private static string LongValidText()
    {
        return "Este documento possui texto suficiente para a analise automatizada, descreve um evento relevante do fundo e traz contexto suficiente para a interpretacao estruturada.";
    }

    private sealed class FakeFileStorage : IFileStorage
    {
        private readonly MemoryStream stream;
        public bool DeleteWasCalledAfterStreamClosed { get; private set; }

        public FakeFileStorage(MemoryStream stream)
        {
            this.stream = stream;
        }

        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            stream.Position = 0;
            return Task.FromResult<Stream>(stream);
        }

        public Task<StoredFile> SaveAsync(Guid documentId, Stream content, string extension, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            DeleteWasCalledAfterStreamClosed = !stream.CanRead;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePdfTextExtractor : IPdfTextExtractor
    {
        private readonly PdfTextExtractionResult result;

        public FakePdfTextExtractor()
            : this(new PdfTextExtractionResult(true, LongValidText(), null))
        {
        }

        public FakePdfTextExtractor(PdfTextExtractionResult result)
        {
            this.result = result;
        }

        public Task<PdfTextExtractionResult> ExtractAsync(Stream pdf, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed class FakeAiAnalysisService : IAiAnalysisService
    {
        public string Model => "fake-model";
        public string PromptVersion => "document-analysis-v1";

        public Task<AiAnalysisResult> AnalyzeAsync(string documentText, PortfolioAnalysisContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new AiAnalysisResult(
                AnalysisImportance.Critical,
                AnalysisClassification.Liquidation,
                true,
                SuggestedAction.Monitor,
                "Resumo objetivo do evento.",
                "Impacto potencial para o cotista.",
                "Impacto considerando a carteira consolidada.",
                new[] { context.Ticker },
                0.92m,
                """{"importance":"Critical"}"""));
        }
    }

    private sealed class ThrowingAiAnalysisService : IAiAnalysisService
    {
        private readonly Exception exception;

        public ThrowingAiAnalysisService(Exception exception)
        {
            this.exception = exception;
        }

        public string Model => "fake-model";
        public string PromptVersion => "document-analysis-v1";

        public Task<AiAnalysisResult> AnalyzeAsync(string documentText, PortfolioAnalysisContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromException<AiAnalysisResult>(exception);
        }
    }
}
