using System.Security.Cryptography;
using Lodira.Application.Abstractions;

namespace Lodira.Infrastructure.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string rootPath;

    public LocalFileStorage(string rootPath)
    {
        this.rootPath = rootPath;
        Directory.CreateDirectory(rootPath);
    }

    public async Task<StoredFile> SaveAsync(
        Guid documentId,
        Stream content,
        string extension,
        CancellationToken cancellationToken = default)
    {
        var normalizedExtension = extension.Trim().TrimStart('.');
        var fileName = string.IsNullOrWhiteSpace(normalizedExtension)
            ? documentId.ToString("N")
            : $"{documentId:N}.{normalizedExtension}";
        var path = Path.Combine(rootPath, fileName);

        await using (var output = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true))
        {
            await content.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
        }

        var hash = await CalculateHashAsync(path, cancellationToken);
        return new StoredFile(path, hash);
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    private static async Task<string> CalculateHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(input, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
