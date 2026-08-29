using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lodira.Domain.Entities;
using DomainPortfolio = Lodira.Domain.Entities.Portfolio;

namespace Lodira.Application.Portfolio;

public static class PortfolioSnapshotKeyCalculator
{
    public static string Calculate(DomainPortfolio portfolio)
        => Calculate(portfolio.Id, portfolio.Positions);

    public static string Calculate(Guid portfolioId, IEnumerable<PortfolioPosition> positions)
    {
        var canonical = new StringBuilder(portfolioId.ToString("D"));
        foreach (var position in positions.OrderBy(x => x.AssetId))
        {
            canonical.Append('|').Append(position.AssetId.ToString("D"));
            Append(canonical, position.Quantity);
            Append(canonical, position.CurrentValue);
            Append(canonical, position.CurrentPrice);
            canonical.Append('|').Append(position.Asset?.AssetClass.ToString() ?? "Unknown");
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, decimal? value)
        => builder.Append('|').Append(value?.ToString("G29", CultureInfo.InvariantCulture) ?? "null");
}
