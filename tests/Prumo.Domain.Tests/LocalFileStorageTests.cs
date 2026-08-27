using System.Text;
using Prumo.Infrastructure.Storage;

namespace Prumo.Domain.Tests;

public sealed class LocalFileStorageTests
{
    [Fact]
    public async Task SaveAsyncFechaOArquivoAntesDeMoverEPermiteReaberturaImediata()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"prumo-storage-{Guid.NewGuid():N}");
        var documentId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("arquivo de teste");

        try
        {
            var storage = new LocalFileStorage(rootPath);
            await using var input = new MemoryStream(content);

            var storedFile = await storage.SaveAsync(documentId, input, "pdf");

            await using var reopened = await storage.OpenReadAsync(storedFile.Path);
            using var reader = new MemoryStream();
            await reopened.CopyToAsync(reader);

            Assert.Equal(content, reader.ToArray());
            Assert.False(string.IsNullOrWhiteSpace(storedFile.Hash));
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
