using Microsoft.Extensions.Logging;
using Prumo.Worker;

namespace Prumo.Domain.Tests;

public sealed class WorkerFileLoggerProviderTests
{
    [Fact]
    public void WritesWarningsAndErrorsToDailyTextFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "prumo-worker-log-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var provider = new WorkerFileLoggerProvider(directory);
            var logger = provider.CreateLogger("Prumo.Worker.Test");
            logger.LogInformation("Informational message");
            logger.LogWarning("Warning message");
            logger.LogError(new InvalidOperationException("Provider failed"), "Error message");
            var file = Assert.Single(Directory.GetFiles(directory, "worker-*.txt"));
            var contents = File.ReadAllText(file);
            Assert.DoesNotContain("Informational message", contents);
            Assert.Contains("Warning message", contents);
            Assert.Contains("Error message", contents);
            Assert.Contains("InvalidOperationException", contents);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void ResolvesLogDirectoryBesideDocuments()
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), "prumo-storage", "documents");
        Assert.Equal(Path.Combine(Path.GetDirectoryName(storageRoot)!, "logs", "worker"), WorkerFileLoggerProvider.ResolveDirectory(storageRoot));
    }
}
