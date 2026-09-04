using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Prumo.Infrastructure.MarketData;

namespace Prumo.Domain.Tests;

public sealed class MarketDataProviderTests
{
    [Fact]
    public async Task AlphaVantage_accepts_close_as_json_string()
    {
        using var http = new HttpClient(new StubHandler("""{"Time Series (Daily)":{"2026-09-04":{"4. close":"31.42"}}}"""));
        var provider = new AlphaVantageMarketDataProvider(http, Options.Create(new AlphaVantageOptions { ApiKey = "test" }));

        var result = await provider.GetHistoryAsync("PETR4", new(2026, 9, 4), new(2026, 9, 4));

        Assert.Equal(31.42m, Assert.Single(result).Close);
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
    }
}
