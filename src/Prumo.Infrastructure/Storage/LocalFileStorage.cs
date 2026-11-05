using System.Security.Cryptography;
using Prumo.Application.Abstractions;

namespace Prumo.Infrastructure.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string rootPath;

    public LocalFileStorage(string rootPath)
    {
        this.rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        Directory.CreateDirectory(this.rootPath);
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(!string.IsNullOrWhiteSpace(path) && File.Exists(ResolveStoragePath(path)));
    }

    public async Task<StoredFile> SaveAsync(
        Guid documentId,
        Stream content,
        string extension,
        CancellationToken cancellationToken = default)
    {
        var normalizedExtension = extension.Trim().TrimStart('.');
        if (normalizedExtension.Length > 16 || normalizedExtension.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException("Extensão de arquivo inválida.", nameof(extension));
        var fileName = string.IsNullOrWhiteSpace(normalizedExtension)
            ? documentId.ToString("N")
            : $"{documentId:N}.{normalizedExtension}";
        var path = Path.Combine(rootPath, fileName);

        var temporaryPath = Path.Combine(rootPath, $".{documentId:N}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 81920, useAsync: true))
            {
                await content.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            var hash = await CalculateHashAsync(temporaryPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: true);
            return new StoredFile(path, hash);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(ResolveStoragePath(path), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(ResolveStoragePath(path));
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<string>> DeleteUnreferencedAsync(IReadOnlySet<string> referencedPaths, TimeSpan minimumAge, CancellationToken cancellationToken = default)
    {
        var deleted = new List<string>();
        var normalizedReferences = referencedPaths.Select(NormalizePath).ToHashSet(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var path in Directory.EnumerateFiles(rootPath, "*", options))
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

    private string ResolveStoragePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(rootPath, fullPath);
        if (relative == "." || relative == ".." || Path.IsPathRooted(relative)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new ArgumentException("O arquivo deve estar dentro do diretório de armazenamento.", nameof(path));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        for (var entry = fullPath; !string.Equals(entry, rootPath, comparison); entry = Path.GetDirectoryName(entry)!)
        {
            if ((File.Exists(entry) || Directory.Exists(entry)) && File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
                throw new ArgumentException("Links não são permitidos no armazenamento de documentos.", nameof(path));
        }
        return fullPath;
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
