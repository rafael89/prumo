using System.Security.Cryptography;
using System.Text;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Entities;

public sealed class Document
{
    private Document()
    {
    }

    public Document(
        Guid assetId,
        DocumentType documentType,
        DateTimeOffset publishedAt,
        string sourceName,
        string? sourceIdentifier,
        string sourceUrl,
        string fingerprint,
        string? downloadUrl = null,
        string? sourceCategory = null,
        string? sourceType = null,
        string? sourceSpecies = null)
    {
        if (assetId == Guid.Empty) throw new ArgumentException("O ativo é obrigatório.", nameof(assetId));
        if (string.IsNullOrWhiteSpace(sourceName)) throw new ArgumentException("A fonte é obrigatória.", nameof(sourceName));
        if (string.IsNullOrWhiteSpace(sourceUrl)) throw new ArgumentException("A origem é obrigatória.", nameof(sourceUrl));
        if (string.IsNullOrWhiteSpace(fingerprint)) throw new ArgumentException("O fingerprint é obrigatório.", nameof(fingerprint));

        Id = Guid.NewGuid();
        AssetId = assetId;
        DocumentType = documentType;
        SourceCategory = Clean(sourceCategory);
        SourceType = Clean(sourceType);
        SourceSpecies = Clean(sourceSpecies);
        PublishedAt = publishedAt.ToUniversalTime();
        SourceName = sourceName.Trim();
        SourceIdentifier = string.IsNullOrWhiteSpace(sourceIdentifier) ? null : sourceIdentifier.Trim();
        SourceUrl = sourceUrl.Trim();
        DownloadUrl = string.IsNullOrWhiteSpace(downloadUrl) ? null : downloadUrl.Trim();
        Fingerprint = fingerprint.Trim().ToLowerInvariant();
        ProcessingStatus = DocumentProcessingStatus.Discovered;
        DiscoveredAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid AssetId { get; private set; }
    public DocumentType DocumentType { get; private set; }
    public DocumentType CanonicalDocumentType => DocumentType;
    public DateTimeOffset PublishedAt { get; private set; }
    public string SourceName { get; private set; } = string.Empty;
    public string? SourceIdentifier { get; private set; }
    public string? SourceCategory { get; private set; }
    public string? SourceType { get; private set; }
    public string? SourceSpecies { get; private set; }
    public string SourceUrl { get; private set; } = string.Empty;
    public string? DownloadUrl { get; private set; }
    public string? FilePath { get; private set; }
    public string? FileHash { get; private set; }
    public string Fingerprint { get; private set; } = string.Empty;
    public DocumentProcessingStatus ProcessingStatus { get; private set; }
    public DateTimeOffset DiscoveredAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public Asset? Asset { get; private set; }

    public void MarkDownloaded(string filePath, string fileHash)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("O caminho do arquivo é obrigatório.", nameof(filePath));
        if (string.IsNullOrWhiteSpace(fileHash)) throw new ArgumentException("O hash do arquivo é obrigatório.", nameof(fileHash));

        FilePath = filePath.Trim();
        FileHash = fileHash.Trim().ToLowerInvariant();
        ProcessingStatus = DocumentProcessingStatus.Downloaded;
    }

    public void MarkWaitingProcessing() => ProcessingStatus = DocumentProcessingStatus.WaitingProcessing;

    public void MarkPendingDownload()
    {
        FilePath = null;
        FileHash = null;
        ProcessedAt = null;
        ProcessingStatus = DocumentProcessingStatus.Discovered;
    }

    public void ReleaseStoredFile()
    {
        FilePath = null;
        FileHash = null;
        if (ProcessingStatus != DocumentProcessingStatus.Error)
        {
            ProcessingStatus = DocumentProcessingStatus.Processed;
        }
    }

    public void MarkProcessing() => ProcessingStatus = DocumentProcessingStatus.Processing;

    public void MarkProcessed()
    {
        ProcessingStatus = DocumentProcessingStatus.Processed;
        ProcessedAt = DateTimeOffset.UtcNow;
    }

    public void MarkError() => ProcessingStatus = DocumentProcessingStatus.Error;
    public void MarkIgnored() => ProcessingStatus = DocumentProcessingStatus.Ignored;

    public void Reclassify(DocumentType documentType)
    {
        if (documentType == Domain.Enums.DocumentType.Other || this.DocumentType == documentType) return;
        DocumentType = documentType;
    }

    public void UpdateSourceMetadata(string? sourceCategory, string? sourceType, string? sourceSpecies, DocumentType canonicalDocumentType)
    {
        SourceCategory = Clean(sourceCategory) ?? SourceCategory;
        SourceType = Clean(sourceType) ?? SourceType;
        SourceSpecies = Clean(sourceSpecies) ?? SourceSpecies;
        if (canonicalDocumentType != DocumentType.Other && (DocumentType == DocumentType.Other || DocumentType != canonicalDocumentType))
        {
            DocumentType = canonicalDocumentType;
        }
    }

    public static string CreateFingerprint(string ticker, DateTimeOffset publishedAt, DocumentType documentType, string sourceName, string? sourceIdentifier, string sourceUrl)
    {
        var sourceIdentity = !string.IsNullOrWhiteSpace(sourceIdentifier)
            ? $"identifier:{sourceIdentifier.Trim()}"
            : $"url:{NormalizeUrl(sourceUrl)}";
        var input = $"{sourceName.Trim().ToUpperInvariant()}|{ticker.Trim().ToUpperInvariant()}|{publishedAt.ToUniversalTime():O}|{sourceIdentity}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeUrl(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return sourceUrl.Trim();
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
            Fragment = string.Empty
        };
        builder.Path = builder.Path.TrimEnd('/');
        return builder.Uri.AbsoluteUri;
    }
}
