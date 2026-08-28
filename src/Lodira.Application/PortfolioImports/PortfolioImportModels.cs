using Lodira.Domain.Enums;

namespace Lodira.Application.PortfolioImports;

public enum SnapshotCompleteness { Complete, Partial, Unknown }
public sealed record NormalizedPortfolioImport(PortfolioImportSource Source, DateTimeOffset? ReferenceDate, SnapshotCompleteness Completeness, IReadOnlyList<NormalizedPortfolioImportItem> Items, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record NormalizedPortfolioImportItem(string Ticker, string AssetName, AssetType AssetType, AssetClass AssetClass, decimal Quantity, decimal? CurrentPrice, decimal? CurrentValue, decimal? AveragePrice, string? Institution, string? Cnpj = null);
public interface IPortfolioImporter
{
    bool CanHandle(string fileName, string? contentType, ReadOnlySpan<byte> header);
    Task<NormalizedPortfolioImport> ParseAsync(Stream content, CancellationToken cancellationToken = default);
}
