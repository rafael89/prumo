using Prumo.Domain.Enums;

namespace Prumo.Application.Abstractions;

public sealed record FinancialDataRequest(Guid AssetId, string Ticker, AssetType AssetType);
public sealed record FinancialDataResult(
    Guid AssetId,
    string Ticker,
    AssetType AssetType,
    DateOnly ReferenceDate,
    FinancialPeriodType PeriodType,
    FinancialDataSource Source,
    DateTimeOffset ObservedAt,
    string? SourceReference = null,
    decimal? Roe = null,
    decimal? NetMargin = null,
    decimal? PriceEarnings = null,
    decimal? PriceBook = null,
    decimal? EvEbitda = null,
    decimal? DividendYield = null);

public interface IFinancialDataProvider
{
    string Name { get; }
    Task<FinancialDataResult?> GetAsync(FinancialDataRequest request, CancellationToken cancellationToken = default);
}
