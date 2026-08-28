using Lodira.Application.Abstractions;

namespace Lodira.Infrastructure.Quotes;

public sealed class ManualQuoteProvider : IQuoteProvider
{
    public Task<decimal?> GetCurrentPriceAsync(string ticker, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<decimal?>(null);
    }
}
