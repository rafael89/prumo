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
    public async Task AlphaVantage_reports_information_limit_response()
    {
        using var http = new HttpClient(new StubHandler("""{"Information":"API call frequency is 5 calls per minute."}"""));
        var provider = new AlphaVantageMarketDataProvider(http, Options.Create(new AlphaVantageOptions { ApiKey = "test" }));

        var error = await Assert.ThrowsAsync<AlphaVantageRateLimitException>(() => provider.GetHistoryAsync("PETR4", new(2026, 9, 4), new(2026, 9, 4)));

        Assert.DoesNotContain("8GHVIU47N1494URL", error.Message);
    }

    [Fact]
    public async Task Brapi_uses_current_historical_endpoint_and_authentication()
    {
        var handler = new StubHandler("""{"results":[{"currency":"BRL","data":{"historicalDataPrice":[{"date":1788476400,"close":31.42}]}}]}""");
        var provider = new BrapiMarketDataProvider(new HttpClient(handler), Options.Create(new BrapiOptions { Token = "test" }), new BrapiMonthlyQuota());

        await provider.GetHistoryAsync("HGLG11", new(2024, 9, 4), new(2026, 9, 4));

        Assert.Contains("/api/v2/stocks/historical", handler.RequestUri!.AbsolutePath);
        Assert.Contains("symbols=HGLG11", handler.RequestUri.Query);
        Assert.Contains("range=3mo", handler.RequestUri.Query);
        Assert.Equal("Bearer test", handler.Authorization);
    }

    [Fact]
    public async Task Brapi_uses_basic_quote_endpoint_for_fiis()
    {
        var handler = new StubHandler("""{"results":[{"historicalDataPrice":[{"date":1788476400,"close":31.42}]}]}""");
        var provider = new BrapiMarketDataProvider(new HttpClient(handler), Options.Create(new BrapiOptions { Token = "test" }), new BrapiMonthlyQuota());

        await provider.GetHistoryAsync("GGRC11", new(2026, 1, 1), new(2026, 9, 4), Prumo.Domain.Enums.AssetType.Fii);

        Assert.Contains("/api/quote/GGRC11", handler.RequestUri!.AbsolutePath);
        Assert.Contains("range=3mo", handler.RequestUri.Query);
    }

    [Fact]
    public async Task Brapi_stops_before_http_when_monthly_quota_is_exhausted()
    {
        var provider = new BrapiMarketDataProvider(new HttpClient(new StubHandler("{}")), Options.Create(new BrapiOptions { Token = "test", MonthlyRequestLimit = 0 }), new BrapiMonthlyQuota());

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetHistoryAsync("HGLG11", new(2026, 9, 4), new(2026, 9, 4)));
    }

    [Fact]
    public async Task Brapi_financial_data_keeps_zero_and_negative_ratios_and_rejects_other_asset_types()
    {
        using var http = new HttpClient(new StubHandler("""{"results":[{"symbol":"PETR4","data":{"defaultKeyStatistics":{"returnOnEquity":{"raw":-2.5},"trailingPE":{"raw":0},"priceToBook":{"raw":1.2},"enterpriseToEbitda":{"raw":null},"dividendYield":{"raw":0}},"financialData":{"profitMargins":{"raw":-0.1}}}}]}"""));
        var provider = new BrapiFinancialDataProvider(http, Options.Create(new BrapiOptions { Token = "test" }), new BrapiMonthlyQuota());

        var result = await provider.GetAsync(new(Guid.NewGuid(), " petr4 ", Prumo.Domain.Enums.AssetType.Stock));
        var unsupported = await provider.GetAsync(new(Guid.NewGuid(), "PETR4", Prumo.Domain.Enums.AssetType.Etf));

        Assert.Equal(-2.5m, result!.Roe);
        Assert.Equal(0m, result.PriceEarnings);
        Assert.Equal(0m, result.DividendYield);
        Assert.Null(result.EvEbitda);
        Assert.Null(unsupported);
    }

    [Fact]
    public async Task Brapi_financial_data_rejects_identity_mismatch()
    {
        using var http = new HttpClient(new StubHandler("""{"results":[{"symbol":"VALE3","data":{"defaultKeyStatistics":{"priceToBook":{"raw":1}}}}]}"""));
        var provider = new BrapiFinancialDataProvider(http, Options.Create(new BrapiOptions { Token = "test" }), new BrapiMonthlyQuota());

        Assert.Null(await provider.GetAsync(new(Guid.NewGuid(), "PETR4", Prumo.Domain.Enums.AssetType.Stock)));
    }

    [Fact]
    public async Task Brapi_discovery_is_bounded_deduplicated_and_exactly_verified()
    {
        var handler = new StubHandler("""{"stocks":[{"stock":"VALE3","name":"Vale","type":"stock"},{"stock":"vale3","name":"Vale duplicada","type":"stock"},{"stock":"PETR4","name":"Petrobras","type":"stock"}]}""", """{"results":[{"symbol":"VALE3"}]}""");
        using var http = new HttpClient(handler);
        var provider = new BrapiAssetDiscoveryProvider(http, Options.Create(new BrapiOptions { Token = "test" }), new BrapiMonthlyQuota());

        var result = await provider.DiscoverAsync(new(Prumo.Domain.Enums.AssetClass.BrazilianStock, new[] { " petr4 " }.ToHashSet(), 5));

        var candidate = Assert.Single(result);
        Assert.Equal("VALE3", candidate.Ticker);
        Assert.Equal(Prumo.Domain.Enums.AssetClass.BrazilianStock, candidate.AssetClass);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public void Candidate_identity_separates_catalog_and_external_keys()
    {
        var assetId = Guid.NewGuid();
        var known = Prumo.Application.Abstractions.CandidateIdentity.Known(assetId);
        var external = Prumo.Application.Abstractions.CandidateIdentity.FromExternal(new("Brapi", "VALE3", "B3", Prumo.Domain.Enums.AssetType.Stock));

        Assert.Equal(assetId, known.AssetId);
        Assert.Null(known.External);
        Assert.Null(external.AssetId);
        Assert.NotEqual(known.Key, external.Key);
        Assert.Throws<ArgumentException>(() => Prumo.Application.Abstractions.CandidateIdentity.Known(Guid.Empty));
    }

    private sealed class StubHandler(params string[] bodies) : HttpMessageHandler
    {
        private readonly Queue<string> responses = new(bodies);
        public int Calls { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responses.Count > 0 ? responses.Dequeue() : "{}", System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
