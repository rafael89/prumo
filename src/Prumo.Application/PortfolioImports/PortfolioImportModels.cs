using Prumo.Domain.Enums;

namespace Prumo.Application.PortfolioImports;

public enum SnapshotCompleteness { Complete, Partial, Unknown }
public sealed record NormalizedPortfolioImport(PortfolioImportSource Source, DateTimeOffset? ReferenceDate, SnapshotCompleteness Completeness, IReadOnlyList<NormalizedPortfolioImportItem> Items, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors)
{
    public IReadOnlyList<NormalizedPortfolioImportTransaction> Transactions { get; init; } = [];
}
public sealed record NormalizedPortfolioImportItem(string Ticker, string AssetName, AssetType AssetType, AssetClass AssetClass, decimal Quantity, decimal? CurrentPrice, decimal? CurrentValue, decimal? AveragePrice, string? Institution, string? Cnpj = null);
public sealed record NormalizedPortfolioImportTransaction(string Ticker, DateTimeOffset OccurredAt, decimal Quantity, decimal UnitPrice, decimal TotalAmount, string? Institution, PortfolioTransactionType Type = PortfolioTransactionType.Buy, FinancialDatePrecision DatePrecision = FinancialDatePrecision.PeriodStart);
public interface IPortfolioImporter
{
    bool CanHandle(string fileName, string? contentType, ReadOnlySpan<byte> header);
    Task<NormalizedPortfolioImport> ParseAsync(Stream content, CancellationToken cancellationToken = default);
}
