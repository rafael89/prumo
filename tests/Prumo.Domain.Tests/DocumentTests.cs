using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class DocumentTests
{
    [Fact]
    public void Inicia_documento_com_status_descoberto()
    {
        var document = CreateDocument();

        Assert.Equal(DocumentProcessingStatus.Discovered, document.ProcessingStatus);
    }

    [Fact]
    public void Fingerprint_e_deterministico_e_independente_de_maiusculas()
    {
        var first = Document.CreateFingerprint("hglg11", DateTimeOffset.Parse("2026-08-27T10:00:00Z"), DocumentType.RelevantFact, "CVM", "123", "https://example.test/one");
        var second = Document.CreateFingerprint("HGLG11", DateTimeOffset.Parse("2026-08-27T10:00:00Z"), DocumentType.RelevantFact, "cvm", "123", "https://example.test/two");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Fingerprint_diferencia_documentos_com_mesma_url_e_metadados_diferentes()
    {
        var first = Document.CreateFingerprint("hglg11", DateTimeOffset.Parse("2026-08-27T10:00:00Z"), DocumentType.RelevantFact, "CVM", null, "https://example.test/document");
        var second = Document.CreateFingerprint("hglg11", DateTimeOffset.Parse("2026-08-28T10:00:00Z"), DocumentType.RelevantFact, "CVM", null, "https://example.test/document");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Download_atualiza_caminho_hash_e_status()
    {
        var document = CreateDocument();

        document.MarkDownloaded("/storage/documents/id/file.pdf", "ABC123");

        Assert.Equal(DocumentProcessingStatus.Downloaded, document.ProcessingStatus);
        Assert.Equal("abc123", document.FileHash);
    }

    [Fact]
    public void Duplicate_link_rejects_self_other_assets_and_chains_and_is_cleared_on_new_download()
    {
        var original = CreateDocument();
        var duplicate = new Document(original.AssetId, DocumentType.Other, DateTimeOffset.UtcNow,
            "CVM", "copy", "https://example.test/copy", "copy");
        Assert.Throws<ArgumentException>(() => original.MarkDuplicate(original));
        Assert.Throws<ArgumentException>(() => duplicate.MarkDuplicate(CreateDocument()));
        duplicate.MarkDuplicate(original);
        Assert.Throws<ArgumentException>(() => original.MarkDuplicate(duplicate));
        duplicate.SetTextHash("old-text-hash");
        duplicate.MarkDownloaded("new.pdf", "new-hash");
        Assert.Null(duplicate.TextHash);
        Assert.Null(duplicate.DuplicateOfDocumentId);
    }

    private static Document CreateDocument()
    {
        return new Document(
            Guid.NewGuid(),
            DocumentType.RelevantFact,
            DateTimeOffset.UtcNow,
            "CVM/FNET",
            "source-1",
            "https://example.test/document",
            "fingerprint");
    }
}
