using Microsoft.EntityFrameworkCore;
using Lodira.Application.Abstractions;
using Lodira.Domain.Entities;
using Lodira.Domain.Rules;
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

    public DocumentAnalysisProcessor(
        LodiraDbContext dbContext,
        IFileStorage fileStorage,
        IPdfTextExtractor pdfTextExtractor,
        ITextQualityValidator textQualityValidator,
        IAiAnalysisService aiAnalysisService,
        ILogger<DocumentAnalysisProcessor> logger)
    {
        this.dbContext = dbContext;
        this.fileStorage = fileStorage;
        this.pdfTextExtractor = pdfTextExtractor;
        this.textQualityValidator = textQualityValidator;
        this.aiAnalysisService = aiAnalysisService;
        this.logger = logger;
    }

    public async Task ProcessAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        var document = job.Document ?? await dbContext.Documents
            .Include(item => item.Asset)
            .SingleOrDefaultAsync(item => item.Id == job.DocumentId, cancellationToken)
            ?? throw new InvalidOperationException("Documento do job nao encontrado.");

        if (await dbContext.DocumentAnalyses.AnyAsync(item => item.ProcessingJobId == job.Id, cancellationToken))
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
        dbContext.DocumentAnalyses.Add(new DocumentAnalysis(
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
            result.RawResponse));

        document.MarkProcessed();
        job.Complete(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Analise persistida com sucesso para o documento {DocumentId} e job {JobId}.", document.Id, job.Id);
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
