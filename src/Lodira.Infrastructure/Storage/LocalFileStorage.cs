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

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<string>> MoveUnreferencedAsync(IReadOnlySet<string> referencedPaths, TimeSpan minimumAge, CancellationToken cancellationToken = default)
    {
        var quarantineRoot = Path.Combine(rootPath, "quarantine", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss"));
        var moved = new List<string>();
        var normalizedReferences = referencedPaths.Select(NormalizePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var quarantinePrefix = NormalizePath(Path.Combine(rootPath, "quarantine")) + Path.DirectorySeparatorChar;
        foreach (var path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NormalizePath(path).StartsWith(quarantinePrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var info = new FileInfo(path);
            if (info.LastWriteTimeUtc > DateTime.UtcNow.Subtract(minimumAge) || normalizedReferences.Contains(NormalizePath(path))) continue;
            if (info.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                moved.Add(path);
                continue;
            }
            var destination = Path.Combine(quarantineRoot, Path.GetRelativePath(rootPath, path));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(path, destination);
            moved.Add(path);
        }
        return moved;
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
