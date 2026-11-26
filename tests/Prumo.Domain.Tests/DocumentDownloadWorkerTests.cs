using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Prumo.Application.PortfolioImports;
using Prumo.Worker;

namespace Prumo.Domain.Tests;

public sealed class DocumentDownloadWorkerTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Recupera520ComNovaRequisicaoEPreservaHeaders(int failures)
    {
        using var handler = new DownloadHandler(failures);
        using var client = new HttpClient(handler);
        using var fixture = new Fixture();

        using var response = await fixture.Worker.SendDownloadWithRetryAsync(client, Guid.NewGuid(),
            "https://fnet.bmfbovespa.com.br/fnet/publico/downloadDocumento?id=123", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(failures + 1, handler.Requests.Count);
        Assert.Equal(handler.Requests.Count, handler.Requests.Distinct().Count());
        Assert.All(handler.Headers, headers =>
        {
            Assert.Contains("application/pdf", headers.Accept);
            Assert.Contains("exibirDocumento?id=123&cvm=true", headers.Referer);
            Assert.Contains("Mozilla/5.0", headers.UserAgent);
        });
    }

    [Theory]
    [InlineData(520, 3)]
    [InlineData(404, 1)]
    public async Task PropagaFalhaComContextoERetentativasLimitadas(int status, int expectedRequests)
    {
        using var handler = new DownloadHandler(int.MaxValue, status);
        using var client = new HttpClient(handler);
        using var fixture = new Fixture();
        var documentId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Worker.SendDownloadWithRetryAsync(
            client, documentId, "https://example.com/document?secret=hidden", TestContext.Current.CancellationToken));

        Assert.Equal((HttpStatusCode)status, exception.StatusCode);
        Assert.Equal(expectedRequests, handler.Requests.Count);
        Assert.Contains(documentId.ToString(), exception.Message);
        Assert.Contains("example.com", exception.Message);
        Assert.DoesNotContain("secret", exception.Message);
    }

    [Fact]
    public async Task CancelamentoInterrompeRetentativa()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var handler = new DownloadHandler(int.MaxValue, onFailure: cts.Cancel);
        using var client = new HttpClient(handler);
        using var fixture = new Fixture();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Worker.SendDownloadWithRetryAsync(
            client, Guid.NewGuid(), "https://example.com/document", cts.Token));

        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Uses_cvm_consultation_download_with_fallback_to_the_published_url(int failures)
    {
        using var handler = new DownloadHandler(failures, status: 404);
        using var client = new HttpClient(handler);
        using var fixture = new Fixture();
        const string publishedUrl = "https://www.rad.cvm.gov.br/ENET/frmDownloadDocumento.aspx?Tela=ext&descTipo=IPE&numProtocolo=1556090&numSequencia=1080796&numVersao=1";

        using var response = await fixture.Worker.SendDownloadWithRetryAsync(client, Guid.NewGuid(), publishedUrl, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(failures + 1, handler.Requests.Count);
        var consultation = handler.Requests[0].RequestUri!;
        Assert.Equal("/ENETCONSULTA/frmDownloadDocumento.aspx", consultation.AbsolutePath);
        Assert.Contains("Tela=99", consultation.Query);
        Assert.Contains("numProtocolo=1556090", consultation.Query);
        if (failures > 0) Assert.Equal(new Uri(publishedUrl), handler.Requests[1].RequestUri);
    }

    private sealed class DownloadHandler(int failures, int status = 520, Action? onFailure = null) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<(string Accept, string Referer, string UserAgent)> Headers { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Headers.Add((request.Headers.Accept.ToString(), request.Headers.Referrer?.ToString() ?? "", request.Headers.UserAgent.ToString()));
            var failed = Requests.Count <= failures;
            if (failed) onFailure?.Invoke();
            return Task.FromResult(new HttpResponseMessage(failed ? (HttpStatusCode)status : HttpStatusCode.OK));
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        private readonly WorkerMetrics metrics = new();
        public DocumentDownloadWorker Worker { get; }

        public Fixture() => Worker = new DocumentDownloadWorker(services.GetRequiredService<IServiceScopeFactory>(),
            new ClientFactory(), Options.Create(new JobProcessingOptions()), NullLogger<DocumentDownloadWorker>.Instance,
            metrics, Options.Create(new DocumentRetentionOptions()));

        public void Dispose()
        {
            Worker.Dispose();
            metrics.Dispose();
            services.Dispose();
        }
    }

    private sealed class ClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException();
    }
}
