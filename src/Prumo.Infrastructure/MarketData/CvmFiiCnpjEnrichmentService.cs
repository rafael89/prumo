using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.DocumentSources;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Infrastructure.MarketData;

public sealed record CvmFiiCnpjEnrichmentResult(
    int EligibleAssets,
    int AssetsWithCnpj,
    int EnrichedAssets,
    int Conflicts,
    int NormalizationFailures);

/// <summary>
/// Resolves missing FII identifiers from the official CVM fund/class registry.
/// Matching is by normalized official fund name and is accepted only when unique.
/// </summary>
public sealed class CvmFiiCnpjEnrichmentService
{
    private readonly PrumoDbContext db;
    private readonly HttpClient httpClient;
    private readonly CvmFnetOptions options;
    private readonly ILogger<CvmFiiCnpjEnrichmentService> logger;

    public CvmFiiCnpjEnrichmentService(PrumoDbContext db, HttpClient httpClient,
        IOptions<CvmFnetOptions> options, ILogger<CvmFiiCnpjEnrichmentService> logger)
    { this.db = db; this.httpClient = httpClient; this.options = options.Value; this.logger = logger; }

    public async Task<CvmFiiCnpjEnrichmentResult> EnrichAsync(CancellationToken cancellationToken = default)
    {
        var assets = await db.Assets
            .Where(x => x.AssetType == AssetType.Fii && x.IsActive
                && db.PortfolioPositions.Any(p => p.AssetId == x.Id && p.Quantity > 0))
            .ToListAsync(cancellationToken);
        var missing = assets.Where(x => string.IsNullOrWhiteSpace(x.Cnpj)).ToArray();
        var result = new CvmFiiCnpjEnrichmentResult(assets.Count, assets.Count - missing.Length, 0, 0, 0);
        if (missing.Length == 0) return result;

        var uri = options.CadastroFiiUrl;
        try
        {
            using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var records = ParseZip(bytes);
            var enriched = 0; var conflicts = 0; var failures = 0;
            foreach (var asset in missing)
            {
                var matches = records.Where(x => x.IsActive && NamesMatch(asset.Name, x.Name)).ToArray();
                if (matches.Length == 0) continue;
                var cnpjs = matches.Select(x => NormalizeCnpj(x.Cnpj)).Where(IsValidCnpj).Distinct(StringComparer.Ordinal).ToArray();
                if (cnpjs.Length != matches.Length) failures++;
                if (cnpjs.Length != 1)
                {
                    if (cnpjs.Length > 1)
                    {
                        conflicts++;
                        logger.LogError("CVM FII identification conflict: ticker={Ticker}, assetName={AssetName}, cnpjs={Cnpjs}", asset.Ticker, asset.Name, string.Join(',', cnpjs));
                    }
                    continue;
                }
                asset.SetCnpj(cnpjs[0]);
                enriched++;
                logger.LogInformation("CVM FII CNPJ enriched: ticker={Ticker}, cnpj={Cnpj}, source={Source}", asset.Ticker, cnpjs[0], uri);
            }
            if (enriched > 0) await db.SaveChangesAsync(cancellationToken);
            return result with { EnrichedAssets = enriched, Conflicts = conflicts, NormalizationFailures = failures };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "CVM FII cadastral enrichment failed: source={Source}", uri);
            throw;
        }
    }

    internal static IReadOnlyList<CvmFiiRegistryRecord> ParseZip(byte[] content)
    {
        using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault(x => x.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("O cadastro CVM não contém CSV.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true);
        var lines = reader.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return [];
        var headers = lines[0].Split(';').Select(x => x.Trim()).ToArray();
        string? Get(IReadOnlyList<string> values, params string[] names)
        {
            foreach (var name in names)
            {
                var index = Array.FindIndex(headers, x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0 && index < values.Count && !string.IsNullOrWhiteSpace(values[index])) return values[index];
            }
            return null;
        }
        return lines.Skip(1).Select(line => line.Split(';'))
            .Select(values => new CvmFiiRegistryRecord(
                Get(values, "CNPJ_Fundo_Classe", "CNPJ_Classe", "CNPJ_Fundo"),
                Get(values, "Denominacao_Social", "Nome_Fundo_Classe", "Nome_Fundo"),
                Get(values, "Situacao", "Situação")))
            .Where(x => !string.IsNullOrWhiteSpace(x.Cnpj) && !string.IsNullOrWhiteSpace(x.Name))
            .ToArray();
    }

    private static bool NamesMatch(string assetName, string? officialName)
    {
        var asset = NormalizeName(assetName);
        var official = NormalizeName(officialName);
        return asset.Length >= 8 && (asset == official || official.StartsWith(asset + " ", StringComparison.Ordinal) || asset.StartsWith(official + " ", StringComparison.Ordinal));
    }

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Normalize(NormalizationForm.FormD);
        var chars = normalized.Select(x => CharUnicodeInfo.GetUnicodeCategory(x) == UnicodeCategory.NonSpacingMark ? ' ' : char.IsLetterOrDigit(x) ? x : ' ').ToArray();
        return Regex.Replace(new string(chars).ToUpperInvariant(), "\\s+", " ").Trim();
    }

    private static string NormalizeCnpj(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());
    private static bool IsValidCnpj(string value) => value.Length == 14 && Regex.IsMatch(value, "^[0-9]{14}$");
}

internal sealed record CvmFiiRegistryRecord(string? Cnpj, string? Name, string? Status)
{
    public bool IsActive => string.IsNullOrWhiteSpace(Status)
        || Status.Contains("ATIVO", StringComparison.OrdinalIgnoreCase)
        || Status.Contains("FUNCIONAMENTO", StringComparison.OrdinalIgnoreCase);
}
