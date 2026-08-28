using Lodira.Domain.Entities;

namespace Lodira.Domain.Rules;

public static class PortfolioCalculator
{
    public static decimal CalculateTotalValue(IEnumerable<PortfolioPosition> positions)
    {
        return positions.Sum(position => position.CurrentValue ?? 0);
    }

    public static void RecalculateWeights(IEnumerable<PortfolioPosition> positions)
    {
        var positionList = positions.ToList();
        var totalValue = CalculateTotalValue(positionList);

        foreach (var position in positionList)
        {
            position.SetPortfolioWeight(totalValue);
        }
    }
}
