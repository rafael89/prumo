using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Prumo.Application.Abstractions;

namespace Prumo.Infrastructure.DocumentSources;

public sealed class CvmFnetDocumentSource : IDocumentSource
{
    private readonly HttpClient httpClient;
    private readonly CvmFnetOptions options;

    public CvmFnetDocumentSource(HttpClient httpClient, IOptions<CvmFnetOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
    }

    public string Name => "CVM/FNET";

    public async Task<IReadOnlyList<DocumentCandidate>> FindAsync(IReadOnlyCollection<string> tickers, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.Endpoint) || tickers.Count == 0)
        {
            return Array.Empty<DocumentCandidate>();
        }

        var uri = $"{options.Endpoint.TrimEnd('/')}/documents?tickers={Uri.EscapeDataString(string.Join(',', tickers))}";
        var candidates = await httpClient.GetFromJsonAsync<List<DocumentCandidate>>(uri, cancellationToken);
        return candidates ?? new List<DocumentCandidate>();
    }
}
