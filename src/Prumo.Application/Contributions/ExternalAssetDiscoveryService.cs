using Prumo.Application.Abstractions;
using Prumo.Domain.Enums;

namespace Prumo.Application.Contributions;

public sealed class ExternalAssetDiscoveryService(IAssetDiscoveryProvider provider)
{
    public Task<IReadOnlyList<DiscoveredAsset>> DiscoverAsync(AssetClass targetClass, IEnumerable<string> knownTickers, CancellationToken cancellationToken = default)
        => provider.DiscoverAsync(new(targetClass, knownTickers.Select(ticker => ticker.Trim().ToUpperInvariant()).ToHashSet(StringComparer.Ordinal), 5), cancellationToken);

}
