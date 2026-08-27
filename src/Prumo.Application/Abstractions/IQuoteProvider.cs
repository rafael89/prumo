namespace Prumo.Application.Abstractions;

public interface IQuoteProvider
{
    Task<decimal?> GetCurrentPriceAsync(string ticker, CancellationToken cancellationToken = default);
}
