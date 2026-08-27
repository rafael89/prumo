namespace Prumo.Application.Abstractions;

public interface IFileStorage
{
    Task<StoredFile> SaveAsync(Guid documentId, Stream content, string extension, CancellationToken cancellationToken = default);
}

public sealed record StoredFile(string Path, string Hash);
