using Microsoft.EntityFrameworkCore;
using Lodira.Application.Abstractions;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Application.Portfolio;
using Lodira.Domain.Rules;
using Lodira.Application.Intelligence;
using Lodira.Infrastructure.Persistence;

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

    public DocumentAnalysisProcessor(
        LodiraDbContext dbContext,
        IFileStorage fileStorage,
        IPdfTextExtractor pdfTextExtractor,
        ITextQualityValidator textQualityValidator,
        IAiAnalysisService aiAnalysisService,
        IPortfolioImpactAssessmentService portfolioImpactAssessmentService,
        ILogger<DocumentAnalysisProcessor> logger)
    {
        this.dbContext = dbContext;
        this.fileStorage = fileStorage;
        this.pdfTextExtractor = pdfTextExtractor;
        this.textQualityValidator = textQualityValidator;
        this.aiAnalysisService = aiAnalysisService;
        this.portfolioImpactAssessmentService = portfolioImpactAssessmentService;
        this.logger = logger;
    }

    public DocumentAnalysisProcessor(
        LodiraDbContext dbContext,
        IFileStorage fileStorage,
        IPdfTextExtractor pdfTextExtractor,
        ITextQualityValidator textQualityValidator,
        IAiAnalysisService aiAnalysisService,
        ILogger<DocumentAnalysisProcessor> logger)
        : this(dbContext, fileStorage, pdfTextExtractor, textQualityValidator, aiAnalysisService, new PortfolioImpactAssessmentService(), logger)
    {
    }

    public async Task ProcessAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        var document = job.Document ?? await dbContext.Documents
            .Include(item => item.Asset)
            .SingleOrDefaultAsync(item => item.Id == job.DocumentId, cancellationToken)
            ?? throw new InvalidOperationException("Documento do job nao encontrado.");

        if (await dbContext.DocumentAnalyses.AnyAsync(item => item.DocumentId == document.Id && item.Model == aiAnalysisService.Model && item.PromptVersion == aiAnalysisService.PromptVersion, cancellationToken))
        {
            document.MarkProcessed();
            job.Complete(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
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

        var context = await BuildPortfolioContextAsync(document.AssetId, cancellationToken);
        logger.LogInformation("Iniciando analise do documento {DocumentId}; providerModel={Model}; promptVersion={PromptVersion}.", document.Id, aiAnalysisService.Model, aiAnalysisService.PromptVersion);

        var result = await aiAnalysisService.AnalyzeAsync(extraction.Text, context, cancellationToken);
        var analysis = new DocumentAnalysis(
            job.Id,
            document.Id,
            aiAnalysisService.Model,
            aiAnalysisService.PromptVersion,
            result.Importance,
            result.Classification,
            result.RequiresAction,
            result.SuggestedAction,
            result.Summary,
            result.Impact,
            result.PortfolioImpact,
            result.AffectedAssets,
            result.Confidence,
            result.RawResponse);
        dbContext.DocumentAnalyses.Add(analysis);
        await PersistEventsAndAssessmentsAsync(analysis, result.Events ?? Array.Empty<AiInvestmentEventResult>(), cancellationToken);

        document.MarkProcessed();
        job.Complete(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Analise persistida com sucesso para o documento {DocumentId} e job {JobId}.", document.Id, job.Id);
    }

    private async Task PersistEventsAndAssessmentsAsync(DocumentAnalysis analysis, IReadOnlyList<AiInvestmentEventResult> results, CancellationToken cancellationToken)
    {
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
                if (targetKey is null) continue;
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

    private async Task<PortfolioAnalysisContext> BuildPortfolioContextAsync(Guid assetId, CancellationToken cancellationToken)
    {
        var asset = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(item => item.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Ativo do documento nao encontrado.");

        var portfolio = await dbContext.Portfolios
            .Include(item => item.Positions)
            .ThenInclude(item => item.Asset)
            .Where(item => item.Positions.Any(position => position.AssetId == assetId))
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (portfolio is null)
        {
            return new PortfolioAnalysisContext(
                Guid.Empty, "Indisponivel", 0, 0, asset.Ticker, asset.AssetType.ToString(), asset.Sector,
                0, 0, null, null, 0, Array.Empty<PortfolioAnalysisPositionContext>(), 0, 0);
        }

        PortfolioCalculator.RecalculateWeights(portfolio.Positions);
        var totalPortfolioValue = PortfolioCalculator.CalculateTotalValue(portfolio.Positions);
        var relatedPosition = portfolio.Positions.SingleOrDefault(item => item.AssetId == assetId);
        var orderedPositions = portfolio.Positions
            .OrderByDescending(item => item.PortfolioWeight)
            .ThenBy(item => item.Asset?.Ticker ?? string.Empty, StringComparer.Ordinal)
            .ToList();
        var topPositions = orderedPositions
            .Take(PortfolioAnalysisContext.MaxTopPositions)
            .Select(item => new PortfolioAnalysisPositionContext(item.Asset?.Ticker ?? string.Empty, item.Asset?.AssetType.ToString() ?? "Indisponivel", item.PortfolioWeight))
            .ToList();
        var largestPositionWeight = orderedPositions.Count == 0 ? 0 : orderedPositions[0].PortfolioWeight;
        var topThreePositionWeight = orderedPositions.Take(3).Sum(item => item.PortfolioWeight);

        return new PortfolioAnalysisContext(
            portfolio.Id,
            portfolio.Name,
            totalPortfolioValue,
            portfolio.Positions.Count,
            asset.Ticker,
            asset.AssetType.ToString(),
            asset.Sector,
            relatedPosition?.Quantity ?? 0,
            relatedPosition?.AveragePrice ?? 0,
            relatedPosition?.CurrentPrice,
            relatedPosition?.CurrentValue,
            relatedPosition?.PortfolioWeight ?? 0,
            topPositions,
            largestPositionWeight,
            topThreePositionWeight);
    }
}
