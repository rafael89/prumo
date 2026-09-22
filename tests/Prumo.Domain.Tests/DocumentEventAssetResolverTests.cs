using Prumo.Application.Documents;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class DocumentEventAssetResolverTests
{
    [Fact]
    public void Adds_canonical_ticker_when_event_mentions_ticker_root()
    {
        var assets = new[]
        {
            new Asset("LVBI11", "LVBI", AssetType.Fii, null),
            new Asset("HGLG11", "HGLG", AssetType.Fii, null)
        };

        var tickers = DocumentEventAssetResolver.MergeExplicitTickers(
            new[] { "LVBI11" },
            "Incorporação do fundo pelo HGLG",
            Array.Empty<string>(),
            assets);

        Assert.Equal(new[] { "HGLG11", "LVBI11" }, tickers);
    }

    [Fact]
    public void Does_not_match_short_or_partial_words()
    {
        var assets = new[] { new Asset("HGLG11", "HGLG", AssetType.Fii, null) };

        var tickers = DocumentEventAssetResolver.MergeExplicitTickers(
            Array.Empty<string>(),
            "O relatório menciona uma obrigação genérica",
            Array.Empty<string>(),
            assets);

        Assert.Empty(tickers);
    }
}
