using Microsoft.EntityFrameworkCore;
using Prumo.Application.Abstractions;
using Prumo.Domain.Entities;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Worker;

public sealed class DocumentAnalysisProcessor
{
    private readonly PrumoDbContext dbContext;
    private readonly IFileStorage fileStorage;
    private readonly IPdfTextExtractor pdfTextExtractor;
    private readonly ITextQualityValidator textQualityValidator;
    private readonly IAiAnalysisService aiAnalysisService;
    private readonly ILogger<DocumentAnalysisProcessor> logger;

    public DocumentAnalysisProcessor(
        PrumoDbContext dbContext,
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

        var positions = await dbContext.PortfolioPositions
            .AsNoTracking()
            .Where(item => item.AssetId == assetId)
            .ToListAsync(cancellationToken);

        var allValuedPositions = await dbContext.PortfolioPositions
            .AsNoTracking()
            .Where(item => item.CurrentValue.HasValue)
            .Select(item => item.CurrentValue!.Value)
            .ToListAsync(cancellationToken);

        var quantity = positions.Sum(item => item.Quantity);
        var totalCost = positions.Sum(item => item.Quantity * item.AveragePrice);
        var averagePrice = quantity > 0 ? totalCost / quantity : 0;
        var currentValue = positions.Sum(item => item.CurrentValue ?? 0m);
        var totalPortfolioValue = allValuedPositions.Sum();
        var portfolioWeight = totalPortfolioValue > 0 ? currentValue / totalPortfolioValue * 100 : 0;

        return new PortfolioAnalysisContext(
            asset.Ticker,
            asset.AssetType.ToString(),
            asset.Sector,
            quantity,
            averagePrice,
            currentValue,
            portfolioWeight);
    }
}
