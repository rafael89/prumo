namespace Lodira.Application.PortfolioImports;

public sealed class DocumentRetentionOptions
{
    public bool KeepPdfAfterAnalysis { get; set; } = true;
    public bool DeletePdfAfterAnalysis { get; set; }
    public int? RetainPdfDays { get; set; }
}
