namespace Lodira.Application.PortfolioImports;

public sealed class DocumentRetentionOptions
{
    public bool KeepPdfAfterAnalysis { get; set; } = true;
    public int? RetainPdfDays { get; set; }
}
