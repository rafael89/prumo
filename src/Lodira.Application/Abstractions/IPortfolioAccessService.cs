namespace Lodira.Application.Abstractions;

/// <summary>Authorization seam; permissive until authentication is enabled.</summary>
public interface IPortfolioAccessService
{
    Task<bool> CanAccessAsync(Guid portfolioId, CancellationToken cancellationToken = default);
}
