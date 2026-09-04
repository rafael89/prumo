using Prumo.Domain.Enums;
using System.Text.Json.Serialization;

namespace Prumo.Application.Abstractions;

public sealed record AssetDiscoveryRequest(AssetClass TargetClass, IReadOnlySet<string> KnownTickers, int MaxResults = 5);
public enum CandidateOrigin { KnownCatalog, ExternalDiscovery }
public sealed record ExternalCandidateIdentity(string Provider, string CanonicalTicker, string? Exchange, AssetType InstrumentType, string? Cnpj = null);
public readonly record struct CandidateKey(string Value)
{
    public override string ToString() => Value;
}
public sealed record CandidateIdentity
{
    public CandidateOrigin Origin { get; }
    public Guid? AssetId { get; }
    public ExternalCandidateIdentity? External { get; }
    public CandidateKey Key { get; }

    [JsonConstructor]
    public CandidateIdentity(CandidateOrigin origin, Guid? assetId, ExternalCandidateIdentity? external)
    {
        if (origin == CandidateOrigin.KnownCatalog && (assetId is null || assetId == Guid.Empty || external is not null)) throw new ArgumentException("Identidade de catálogo inválida.");
        if (origin == CandidateOrigin.ExternalDiscovery && (assetId is not null || external is null)) throw new ArgumentException("Identidade externa inválida.");
        Origin = origin; AssetId = assetId; External = external;
        Key = new(origin == CandidateOrigin.KnownCatalog ? $"KnownCatalog:{assetId}" : $"ExternalDiscovery:{external!.Provider}:{external.Exchange}:{external.CanonicalTicker}");
    }

    public static CandidateIdentity Known(Guid assetId) => new(CandidateOrigin.KnownCatalog, assetId, null);
    public static CandidateIdentity FromExternal(ExternalCandidateIdentity external) => new(CandidateOrigin.ExternalDiscovery, null, external);
}
public sealed record DiscoveredAsset(string Provider, string Ticker, string Name, AssetType AssetType, AssetClass? AssetClass, string? Exchange, string? Currency, string? Sector, string? SourceReference)
{
    public CandidateOrigin Origin => CandidateOrigin.ExternalDiscovery;
    public ExternalCandidateIdentity Identity => new(Provider, Ticker, Exchange, AssetType);
}
public interface IAssetDiscoveryProvider
{
    string Name { get; }
    Task<IReadOnlyList<DiscoveredAsset>> DiscoverAsync(AssetDiscoveryRequest request, CancellationToken cancellationToken = default);
}
