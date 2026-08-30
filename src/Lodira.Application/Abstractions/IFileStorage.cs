namespace Lodira.Application.Abstractions;

public interface IFileStorage
{
    Task<StoredFile> SaveAsync(Guid documentId, Stream content, string extension, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default);
    Task DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task<IReadOnlyList<string>> MoveUnreferencedAsync(IReadOnlySet<string> referencedPaths, TimeSpan minimumAge, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}

public sealed record StoredFile(string Path, string Hash);
