using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Lodira.Application.Abstractions;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Application.Portfolio;
using Lodira.Domain.Rules;
using Lodira.Application.Intelligence;
using Lodira.Application.PortfolioImports;
using Lodira.Infrastructure.Persistence;
using Npgsql;

namespace Lodira.Worker;

public sealed class DocumentAnalysisProcessor
{
    private readonly LodiraDbContext dbContext;
    private readonly IFileStorage fileStorage;
    private readonly IPdfTextExtractor pdfTextExtractor;
    private readonly ITextQualityValidator textQualityValidator;
    private readonly IAiAnalysisService aiAnalysisService;
    private readonly ILogger<DocumentAnalysisProcessor> logger;
    private readonly IPortfolioImpactAssessmentService portfolioImpactAssessmentService;
    private readonly DocumentRetentionOptions retention;

    public DocumentAnalysisProcessor(
        LodiraDbContext dbContext,
        IFileStorage fileStorage,
        IPdfTextExtractor pdfTextExtractor,
        ITextQualityValidator textQualityValidator,
        IAiAnalysisService aiAnalysisService,
        IPortfolioImpactAssessmentService portfolioImpactAssessmentService,
        ILogger<DocumentAnalysisProcessor> logger,
        IOptions<DocumentRetentionOptions> retention)
    {
        this.dbContext = dbContext;
        this.fileStorage = fileStorage;
        this.pdfTextExtractor = pdfTextExtractor;
        this.textQualityValidator = textQualityValidator;
        this.aiAnalysisService = aiAnalysisService;
        this.portfolioImpactAssessmentService = portfolioImpactAssessmentService;
        this.logger = logger;
        this.retention = retention.Value;
    }

    public DocumentAnalysisProcessor(
        LodiraDbContext dbContext,
        IFileStorage fileStorage,
        IPdfTextExtractor pdfTextExtractor,
        ITextQualityValidator textQualityValidator,
        IAiAnalysisService aiAnalysisService,
        ILogger<DocumentAnalysisProcessor> logger)
        : this(dbContext, fileStorage, pdfTextExtractor, textQualityValidator, aiAnalysisService, new PortfolioImpactAssessmentService(), logger, Options.Create(new DocumentRetentionOptions()))
    {
    }

    public async Task ProcessAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        var document = job.Document ?? await dbContext.Documents
            .Include(item => item.Asset)
            .SingleOrDefaultAsync(item => item.Id == job.DocumentId, cancellationToken)
            ?? throw new InvalidOperationException("Documento do job nao encontrado.");

        var existingAnalysis = await dbContext.DocumentAnalyses
            .Include(item => item.Document)
            .ThenInclude(item => item!.Asset)
            .FirstOrDefaultAsync(item => item.DocumentId == document.Id && aiAnalysisService.AcceptedModels.Contains(item.Model) && item.PromptVersion == aiAnalysisService.PromptVersion, cancellationToken);
        if (existingAnalysis is not null)
        {
            await EnsureExistingAnalysisFeedsRadarAsync(existingAnalysis, cancellationToken);
            document.MarkProcessed();
            job.Complete(DateTimeOffset.UtcNow);
            await SaveChangesHandlingConcurrentAssessmentsAsync(cancellationToken);
            logger.LogInformation("Analise ja persistida para o job {JobId}; finalizando de forma idempotente.", job.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(document.FilePath))
        {
            throw new InvalidOperationException("Documento sem arquivo armazenado.");
        }

        document.MarkProcessing();
        logger.LogInformation("Iniciando extracao do documento {DocumentId} para o job {JobId}.", document.Id, job.Id);

        await using var pdfStream = await fileStorage.OpenReadAsync(document.FilePath, cancellationToken);
        var extraction = await pdfTextExtractor.ExtractAsync(pdfStream, cancellationToken);
        if (!extraction.Succeeded)
        {
            logger.LogWarning("Falha na extracao do documento {DocumentId}: {Error}", document.Id, extraction.Error);
            throw new InvalidOperationException(extraction.Error ?? "Falha ao extrair texto do PDF.");
        }

        logger.LogInformation("Extracao concluida para o documento {DocumentId}; caracteres={CharacterCount}.", document.Id, extraction.Text.Length);
        var quality = textQualityValidator.Validate(extraction.Text);
        if (!quality.IsValid)
        {
            logger.LogWarning("Texto rejeitado para o documento {DocumentId}; caracteres={CharacterCount}; printableRatio={PrintableRatio}; motivo={Error}", document.Id, quality.CharacterCount, quality.PrintableCharacterRatio, quality.Error);
            throw new InvalidOperationException(quality.Error ?? "Texto do documento invalido.");
        }

        var context = await BuildDocumentContextAsync(document.AssetId, cancellationToken);
        logger.LogInformation("Iniciando analise do documento {DocumentId}; providerModel={Model}; promptVersion={PromptVersion}.", document.Id, aiAnalysisService.Model, aiAnalysisService.PromptVersion);

        var result = await aiAnalysisService.AnalyzeAsync(extraction.Text, context, cancellationToken);
        var analysis = new DocumentAnalysis(
            job.Id,
            document.Id,
            result.Model ?? aiAnalysisService.Model,
            aiAnalysisService.PromptVersion,
            result.Importance,
            result.Classification,
            result.RequiresAction,
            result.SuggestedAction,
            result.Summary,
            result.Impact,
            "O impacto na carteira é calculado separadamente para cada carteira.",
            result.AffectedAssets,
            result.Confidence,
            result.RawResponse);
        dbContext.DocumentAnalyses.Add(analysis);
        await PersistEventsAndAssessmentsAsync(analysis, result.Events ?? Array.Empty<AiInvestmentEventResult>(), cancellationToken);

        document.MarkProcessed();
        job.Complete(DateTimeOffset.UtcNow);
        await SaveChangesHandlingConcurrentAssessmentsAsync(cancellationToken);
        await ReleasePdfAfterAnalysisAsync(document, cancellationToken);
        logger.LogInformation("Analise persistida com sucesso para o documento {DocumentId} e job {JobId}.", document.Id, job.Id);
    }

    private async Task ReleasePdfAfterAnalysisAsync(Document document, CancellationToken cancellationToken)
    {
        if (!retention.DeletePdfAfterAnalysis || string.IsNullOrWhiteSpace(document.FilePath)) return;

        var path = document.FilePath;
        var shared = await dbContext.Documents.AsNoTracking()
            .AnyAsync(item => item.Id != document.Id && item.FilePath == path, cancellationToken);
        if (!shared) await fileStorage.DeleteAsync(path, cancellationToken);

        document.ReleaseStoredFile();
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("PDF temporário liberado para o documento {DocumentId}; compartilhado={Shared}.", document.Id, shared);
    }

    private async Task SaveChangesHandlingConcurrentAssessmentsAsync(CancellationToken cancellationToken)
    {
        RemoveDuplicatePendingAssessments(dbContext);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsPortfolioAssessmentUniqueViolation(exception))
        {
            var pendingAssessments = dbContext.ChangeTracker.Entries<PortfolioAssessment>()
                .Where(entry => entry.State == EntityState.Added)
                .Select(entry => entry.Entity)
                .ToList();

            if (pendingAssessments.Count == 0) throw;

            foreach (var assessment in pendingAssessments) dbContext.Entry(assessment).State = EntityState.Detached;
            foreach (var assessment in pendingAssessments
                         .GroupBy(item => new { item.PortfolioId, item.InvestmentEventId, item.PortfolioSnapshotKey, item.TargetAllocationKey, item.AssessmentVersion })
                         .Select(group => group.First()))
            {
                var alreadyPersisted = await dbContext.PortfolioAssessments.AnyAsync(item =>
                    item.PortfolioId == assessment.PortfolioId
                    && item.InvestmentEventId == assessment.InvestmentEventId
                    && item.PortfolioSnapshotKey == assessment.PortfolioSnapshotKey
                    && item.TargetAllocationKey == assessment.TargetAllocationKey
                    && item.AssessmentVersion == assessment.AssessmentVersion, cancellationToken);
                if (!alreadyPersisted) dbContext.PortfolioAssessments.Add(assessment);
            }
            logger.LogInformation("Avaliação de carteira concorrente ignorada; o registro já existente foi preservado.");
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    internal static void RemoveDuplicatePendingAssessments(LodiraDbContext dbContext)
    {
        var duplicatePendingAssessments = dbContext.ChangeTracker.Entries<PortfolioAssessment>()
            .Where(entry => entry.State == EntityState.Added)
            .GroupBy(entry => new
            {
                entry.Entity.PortfolioId,
                entry.Entity.InvestmentEventId,
                entry.Entity.PortfolioSnapshotKey,
                entry.Entity.TargetAllocationKey,
                entry.Entity.AssessmentVersion
            })
            .SelectMany(group => group.Skip(1))
            .ToList();

        foreach (var entry in duplicatePendingAssessments) entry.State = EntityState.Detached;
    }

    private static bool IsPortfolioAssessmentUniqueViolation(DbUpdateException exception)
        => exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: not null } postgresException
            && postgresException.ConstraintName.StartsWith("IX_PortfolioAssessments_", StringComparison.Ordinal);

    private async Task EnsureExistingAnalysisFeedsRadarAsync(DocumentAnalysis analysis, CancellationToken cancellationToken)
    {
        if (analysis.Importance is not (AnalysisImportance.Critical or AnalysisImportance.Relevant)) return;
        var existingEvents = await dbContext.InvestmentEvents.Include(item => item.AffectedAssets).Where(item => item.DocumentAnalysisId == analysis.Id).ToListAsync(cancellationToken);
        if (existingEvents.Count > 0)
        {
            foreach (var investmentEvent in existingEvents) await EnsureAssessmentsForEventAsync(analysis, investmentEvent, cancellationToken);
            return;
        }
        var document = analysis.Document;
        var ticker = document?.Asset?.Ticker;
        if (document is null || string.IsNullOrWhiteSpace(ticker)) return;
        await PersistEventsAndAssessmentsAsync(analysis, new[]
        {
            new AiInvestmentEventResult(InvestmentEventType.Other, document.PublishedAt, analysis.Summary, Array.Empty<string>(), new[] { ticker })
        }, cancellationToken);
        logger.LogInformation("Evento documental reconstruído para análise já persistida {AnalysisId}.", analysis.Id);
    }

    private async Task EnsureAssessmentsForEventAsync(DocumentAnalysis analysis, InvestmentEvent investmentEvent, CancellationToken cancellationToken)
    {
        var assetIds = investmentEvent.AffectedAssets.Select(asset => asset.Id).ToArray();
        if (assetIds.Length == 0) return;
        var portfolios = await dbContext.Portfolios.Include(portfolio => portfolio.Positions).ThenInclude(position => position.Asset).Where(portfolio => portfolio.Positions.Any(position => assetIds.Contains(position.AssetId))).ToListAsync(cancellationToken);
        foreach (var portfolio in portfolios)
        {
            var targets = await dbContext.TargetAllocations.Where(target => target.PortfolioId == portfolio.Id).ToListAsync(cancellationToken);
            var targetKey = TargetAllocationKeyCalculator.Calculate(targets);
            var health = targetKey is null ? null : new PortfolioHealthCalculator().Calculate(portfolio, targets);
            var relevantPositions = portfolio.Positions.Where(position => assetIds.Contains(position.AssetId)).Select(position => new RelevantPositionContext(position.AssetId, position.Asset?.Ticker ?? string.Empty, position.Asset?.AssetClass ?? AssetClass.Other, position.CurrentValue ?? 0, position.PortfolioWeight)).ToList();
            var output = portfolioImpactAssessmentService.AssessPortfolioImpact(new PortfolioImpactAssessmentContext(investmentEvent, portfolio, relevantPositions, health));
            var snapshotKey = PortfolioSnapshotKeyCalculator.Calculate(portfolio);
            if (await dbContext.PortfolioAssessments.AnyAsync(item => item.PortfolioId == portfolio.Id && item.InvestmentEventId == investmentEvent.Id && item.PortfolioSnapshotKey == snapshotKey && item.TargetAllocationKey == targetKey && item.AssessmentVersion == portfolioImpactAssessmentService.Version, cancellationToken)) continue;
            dbContext.PortfolioAssessments.Add(new PortfolioAssessment(portfolio.Id, investmentEvent.Id, output.AttentionPriority, output.ActionRequirement, output.CapitalDecisionImpact, AssessmentStatus.Detected, AssessmentUserState.Unread, output.Summary, output.WhyItMatters, output.SuggestedBehavior, output.Confidence, targetKey, portfolioImpactAssessmentService.Version, snapshotKey, investmentEvent.Version));
        }
    }

    private async Task PersistEventsAndAssessmentsAsync(DocumentAnalysis analysis, IReadOnlyList<AiInvestmentEventResult> results, CancellationToken cancellationToken)
    {
        if (results.Count == 0 && analysis.Importance is AnalysisImportance.Critical or AnalysisImportance.Relevant)
        {
            var analyzedDocument = await dbContext.Documents.Include(document => document.Asset).SingleAsync(document => document.Id == analysis.DocumentId, cancellationToken);
            var ticker = analyzedDocument.Asset?.Ticker;
            if (!string.IsNullOrWhiteSpace(ticker))
            {
                results = new[]
                {
                    new AiInvestmentEventResult(InvestmentEventType.Other, analyzedDocument.PublishedAt, analysis.Summary, Array.Empty<string>(), new[] { ticker })
                };
                logger.LogInformation("Nenhum evento estruturado foi retornado; criado evento documental para {DocumentId}.", analysis.DocumentId);
            }
        }
        foreach (var result in results)
        {
            var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{result.EventType}|{result.OccurredAt:O}|{result.Summary.Trim()}|{string.Join("|", result.Facts)}"))).ToLowerInvariant();
            var investmentEvent = await dbContext.InvestmentEvents.Include(x => x.AffectedAssets).SingleOrDefaultAsync(x => x.DocumentAnalysisId == analysis.Id && x.Fingerprint == fingerprint, cancellationToken);
            if (investmentEvent is null)
            {
                investmentEvent = new InvestmentEvent(analysis.Id, result.EventType, result.OccurredAt, result.Summary, result.Facts);
                var assets = await dbContext.Assets.Where(x => result.AffectedAssets.Contains(x.Ticker)).ToListAsync(cancellationToken);
                investmentEvent.AffectedAssets.AddRange(assets);
                investmentEvent.Evidence.Add(new InvestmentEventEvidence(investmentEvent.Id, analysis.Id, analysis.DocumentId, "Análise estruturada do documento"));
                dbContext.InvestmentEvents.Add(investmentEvent);
            }

            var assetIds = investmentEvent.AffectedAssets.Select(x => x.Id).ToArray();
            if (assetIds.Length == 0) continue;
            var portfolios = await dbContext.Portfolios.Include(x => x.Positions).ThenInclude(x => x.Asset).Where(x => x.Positions.Any(p => assetIds.Contains(p.AssetId))).ToListAsync(cancellationToken);
            foreach (var portfolio in portfolios)
            {
                var targets = await dbContext.TargetAllocations.Where(x => x.PortfolioId == portfolio.Id).ToListAsync(cancellationToken);
                var targetKey = TargetAllocationKeyCalculator.Calculate(targets);
                var health = targetKey is null ? null : new PortfolioHealthCalculator().Calculate(portfolio, targets);
                var relevantPositions = portfolio.Positions
                    .Where(x => assetIds.Contains(x.AssetId))
                    .Select(x => new RelevantPositionContext(x.AssetId, x.Asset?.Ticker ?? string.Empty, x.Asset?.AssetClass ?? AssetClass.Other, x.CurrentValue ?? 0, x.PortfolioWeight))
                    .ToList();
                var output = portfolioImpactAssessmentService.AssessPortfolioImpact(new PortfolioImpactAssessmentContext(investmentEvent, portfolio, relevantPositions, health));
                var assessmentVersion = portfolioImpactAssessmentService.Version;
                var portfolioSnapshotKey = PortfolioSnapshotKeyCalculator.Calculate(portfolio);
                if (await dbContext.PortfolioAssessments.AnyAsync(x => x.PortfolioId == portfolio.Id && x.InvestmentEventId == investmentEvent.Id && x.PortfolioSnapshotKey == portfolioSnapshotKey && x.TargetAllocationKey == targetKey && x.AssessmentVersion == assessmentVersion, cancellationToken)) continue;
                logger.LogInformation("PortfolioAssessment reevaluation requested PortfolioId={PortfolioId} AssessmentVersion={AssessmentVersion} PortfolioSnapshotKey={PortfolioSnapshotKey} InvestmentEventId={InvestmentEventId} InvestmentEventVersion={InvestmentEventVersion}", portfolio.Id, assessmentVersion, portfolioSnapshotKey, investmentEvent.Id, investmentEvent.Version);
                dbContext.PortfolioAssessments.Add(new PortfolioAssessment(portfolio.Id, investmentEvent.Id, output.AttentionPriority, output.ActionRequirement, output.CapitalDecisionImpact, AssessmentStatus.Detected, AssessmentUserState.Unread, output.Summary, output.WhyItMatters, output.SuggestedBehavior, output.Confidence, targetKey, portfolioImpactAssessmentService.Version, portfolioSnapshotKey, investmentEvent.Version));
            }
        }
    }

    private async Task<PortfolioAnalysisContext> BuildDocumentContextAsync(Guid assetId, CancellationToken cancellationToken)
    {
        var asset = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(item => item.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Ativo do documento nao encontrado.");

        // Document analysis is global. Never load a portfolio here: the same
        // analysis can be consumed by multiple portfolios and users.
        return new PortfolioAnalysisContext(
            Guid.Empty, string.Empty, 0, 0, asset.Ticker, asset.AssetType.ToString(), asset.Sector,
            0, 0, null, null, 0, Array.Empty<PortfolioAnalysisPositionContext>(), 0, 0);
    }
}
