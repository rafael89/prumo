using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lodira.Domain.Entities;
namespace Lodira.Application.Portfolio;
public static class TargetAllocationKeyCalculator
{
    public static string? Calculate(IEnumerable<TargetAllocation>? allocations)
    {
        var values = allocations?.OrderBy(x=>x.AssetClass).Select(x=>$"{(int)x.AssetClass}:{x.TargetPercentage.ToString("G29",CultureInfo.InvariantCulture)}").ToArray();
        if (values is null || values.Length == 0) return null;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",values)))).ToLowerInvariant();
    }
}
