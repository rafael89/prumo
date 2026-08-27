using Prumo.Application.Abstractions;

namespace Prumo.Infrastructure.Quotes;

public sealed class ManualQuoteProvider : IQuoteProvider
{
    public Task<decimal?> GetCurrentPriceAsync(string ticker, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<decimal?>(null);
    }
}
