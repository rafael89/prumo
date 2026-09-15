using System.Security.Cryptography;
using Prumo.Application.Abstractions;

namespace Prumo.Infrastructure.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string rootPath;

    public LocalFileStorage(string rootPath)
    {
        this.rootPath = rootPath;
        Directory.CreateDirectory(rootPath);
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(!string.IsNullOrWhiteSpace(path) && File.Exists(path));
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

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<string>> DeleteUnreferencedAsync(IReadOnlySet<string> referencedPaths, TimeSpan minimumAge, CancellationToken cancellationToken = default)
    {
        var deleted = new List<string>();
        var normalizedReferences = referencedPaths.Select(NormalizePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if (info.LastWriteTimeUtc > DateTime.UtcNow.Subtract(minimumAge) || normalizedReferences.Contains(NormalizePath(path))) continue;
            File.Delete(path);
            deleted.Add(path);
        }
        return deleted;
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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
