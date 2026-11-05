using System.Text;
using Prumo.Infrastructure.Storage;

namespace Prumo.Domain.Tests;

public sealed class LocalFileStorageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_save_never_publishes_partial_content(bool existingFile)
    {
        var root = Path.Combine(Path.GetTempPath(), $"prumo-storage-{Guid.NewGuid():N}");
        try
        {
            var storage = new LocalFileStorage(root);
            var id = Guid.NewGuid();
            var path = Path.Combine(root, $"{id:N}.pdf");
            if (existingFile) await storage.SaveAsync(id, new MemoryStream("original"u8.ToArray()), "pdf", TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<IOException>(() => storage.SaveAsync(id, new BrokenStream(), "pdf", TestContext.Current.CancellationToken));

            if (existingFile) Assert.Equal("original", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            else Assert.False(File.Exists(path));
            Assert.Equal(existingFile ? 1 : 0, Directory.GetFiles(root).Length);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Storage_rejects_paths_outside_its_root_and_invalid_extensions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"prumo-storage-{Guid.NewGuid():N}");
        var outside = root + "-outside.txt";
        try
        {
            var storage = new LocalFileStorage(root);
            await File.WriteAllTextAsync(outside, "keep", TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(outside, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ArgumentException>(() => storage.DeleteAsync(outside, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveAsync(Guid.NewGuid(), new MemoryStream(), "pdf/../../outside", TestContext.Current.CancellationToken));
            Assert.Equal("keep", await File.ReadAllTextAsync(outside, TestContext.Current.CancellationToken));
        }
        finally { File.Delete(outside); Directory.Delete(root, recursive: true); }
    }

    private sealed class BrokenStream : MemoryStream
    {
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            await destination.WriteAsync("partial"u8.ToArray(), cancellationToken);
            throw new IOException("Interrupted download");
        }
    }

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

            var storedFile = await storage.SaveAsync(documentId, input, "pdf", TestContext.Current.CancellationToken);

            await using var reopened = await storage.OpenReadAsync(storedFile.Path, TestContext.Current.CancellationToken);
            using var reader = new MemoryStream();
            await reopened.CopyToAsync(reader, TestContext.Current.CancellationToken);

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
