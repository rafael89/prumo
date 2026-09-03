using Prumo.Domain.Enums;

namespace Prumo.Domain.Entities;

public sealed class DocumentAnalysis
{
    private DocumentAnalysis()
    {
    }

    public DocumentAnalysis(
        Guid processingJobId,
        Guid documentId,
        string model,
        string promptVersion,
        AnalysisImportance importance,
        AnalysisClassification classification,
        bool requiresAction,
        SuggestedAction suggestedAction,
        string summary,
        string impact,
        string portfolioImpact,
        IEnumerable<string> affectedAssets,
        decimal confidence,
        string rawResponse)
    {
        if (processingJobId == Guid.Empty) throw new ArgumentException("O job de processamento e obrigatorio.", nameof(processingJobId));
        if (documentId == Guid.Empty) throw new ArgumentException("O documento e obrigatorio.", nameof(documentId));
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("O modelo e obrigatorio.", nameof(model));
        if (string.IsNullOrWhiteSpace(promptVersion)) throw new ArgumentException("A versao do prompt e obrigatoria.", nameof(promptVersion));
        if (string.IsNullOrWhiteSpace(summary)) throw new ArgumentException("O resumo e obrigatorio.", nameof(summary));
        if (string.IsNullOrWhiteSpace(impact)) throw new ArgumentException("O impacto e obrigatorio.", nameof(impact));
        if (string.IsNullOrWhiteSpace(portfolioImpact)) throw new ArgumentException("O impacto na carteira e obrigatorio.", nameof(portfolioImpact));
        if (confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(confidence));
        if (string.IsNullOrWhiteSpace(rawResponse)) throw new ArgumentException("A resposta original e obrigatoria.", nameof(rawResponse));

        Id = Guid.NewGuid();
        ProcessingJobId = processingJobId;
        DocumentId = documentId;
        Model = model.Trim();
        PromptVersion = promptVersion.Trim();
        Importance = importance;
        Classification = classification;
        RequiresAction = requiresAction;
        SuggestedAction = suggestedAction;
        Summary = summary.Trim();
        Impact = impact.Trim();
        // Kept for database compatibility; portfolio impact is now produced by
        // PortfolioAssessment per portfolio and is not sourced from the AI.
        PortfolioImpact = portfolioImpact.Trim();
        AffectedAssets = affectedAssets
            .Where(asset => !string.IsNullOrWhiteSpace(asset))
            .Select(asset => asset.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();
        Confidence = confidence;
        RawResponse = rawResponse;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid ProcessingJobId { get; private set; }
    public Guid DocumentId { get; private set; }
    public string Model { get; private set; } = string.Empty;
    public string PromptVersion { get; private set; } = string.Empty;
    public AnalysisImportance Importance { get; private set; }
    public AnalysisClassification Classification { get; private set; }
    public bool RequiresAction { get; private set; }
    public SuggestedAction SuggestedAction { get; private set; }
    public string Summary { get; private set; } = string.Empty;
    public string Impact { get; private set; } = string.Empty;
    public string PortfolioImpact { get; private set; } = string.Empty;
    public List<string> AffectedAssets { get; private set; } = new();
    public decimal Confidence { get; private set; }
    public string RawResponse { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public Document? Document { get; private set; }
}
