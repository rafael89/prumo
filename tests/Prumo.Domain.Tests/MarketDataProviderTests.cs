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

    [Fact]
    public async Task Brapi_uses_current_historical_endpoint_and_authentication()
    {
        var handler = new StubHandler("""{"results":[{"currency":"BRL","data":{"historicalDataPrice":[{"date":1788476400,"close":31.42}]}}]}""");
        var provider = new BrapiMarketDataProvider(new HttpClient(handler), Options.Create(new BrapiOptions { Token = "test" }), new BrapiMonthlyQuota());

        await provider.GetHistoryAsync("HGLG11", new(2024, 9, 4), new(2026, 9, 4));

        Assert.Contains("/api/v2/stocks/historical", handler.RequestUri!.AbsolutePath);
        Assert.Contains("symbols=HGLG11", handler.RequestUri.Query);
        Assert.Equal("Bearer test", handler.Authorization);
    }

    [Fact]
    public async Task Brapi_stops_before_http_when_monthly_quota_is_exhausted()
    {
        var provider = new BrapiMarketDataProvider(new HttpClient(new StubHandler("{}")), Options.Create(new BrapiOptions { Token = "test", MonthlyRequestLimit = 0 }), new BrapiMonthlyQuota());

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetHistoryAsync("HGLG11", new(2026, 9, 4), new(2026, 9, 4)));
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
