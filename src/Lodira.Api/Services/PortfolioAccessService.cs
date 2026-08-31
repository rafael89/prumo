using Microsoft.EntityFrameworkCore;
using Lodira.Application.Abstractions;
using Lodira.Infrastructure.Persistence;

namespace Lodira.Api.Services;

public sealed class PortfolioAccessService(LodiraDbContext db) : IPortfolioAccessService
{
    public Task<bool> CanAccessAsync(Guid portfolioId, CancellationToken cancellationToken = default) =>
        db.Portfolios.AnyAsync(portfolio => portfolio.Id == portfolioId, cancellationToken);
}
