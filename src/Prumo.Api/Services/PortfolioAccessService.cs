using Microsoft.EntityFrameworkCore;
using Prumo.Application.Abstractions;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Api.Services;

public sealed class PortfolioAccessService(PrumoDbContext db) : IPortfolioAccessService
{
    public Task<bool> CanAccessAsync(Guid portfolioId, CancellationToken cancellationToken = default) =>
        db.Portfolios.AnyAsync(portfolio => portfolio.Id == portfolioId, cancellationToken);
}
