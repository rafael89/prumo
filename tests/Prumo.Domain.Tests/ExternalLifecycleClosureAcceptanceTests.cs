using System.Net;
using Microsoft.Extensions.Options;
using Prumo.Infrastructure.MarketData;

namespace Prumo.Domain.Tests;

public sealed class ExternalLifecycleClosureAcceptanceTests
{
    [Fact]
    public async Task Discovery_outage_returns_no_external_candidate()
    {
        var provider = new BrapiAssetDiscoveryProvider(new HttpClient(new FailingHandler()), Options.Create(new BrapiOptions()), new BrapiMonthlyQuota());
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.DiscoverAsync(new(Prumo.Domain.Enums.AssetClass.BrazilianStock, new HashSet<string>(), 5)));
    }

    [Fact]
    public async Task Verification_failure_discards_raw_candidate()
    {
        using var http = new HttpClient(new ResponseHandler(
            "{\"stocks\":[{\"stock\":\"EXT11\",\"name\":\"Externa\"}]}",
            "{\"results\":[{\"symbol\":\"OTHER3\"}]}"));
        var provider = new BrapiAssetDiscoveryProvider(http, Options.Create(new BrapiOptions()), new BrapiMonthlyQuota());
        var result = await provider.DiscoverAsync(new(Prumo.Domain.Enums.AssetClass.BrazilianStock, new HashSet<string>(), 5));
        Assert.Empty(result);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new HttpRequestException("provider unavailable");
    }

    private sealed class ResponseHandler(params string[] bodies) : HttpMessageHandler
    {
        private readonly Queue<string> responses = new(bodies);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responses.Dequeue()) });
    }
}
