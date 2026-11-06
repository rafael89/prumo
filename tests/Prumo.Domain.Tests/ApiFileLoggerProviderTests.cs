using Microsoft.Extensions.Logging;
using Prumo.Api;

namespace Prumo.Domain.Tests;

public sealed class ApiFileLoggerProviderTests
{
    [Fact]
    public void WritesWarningsAndErrorsToDailyTextFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "prumo-api-log-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var provider = new ApiFileLoggerProvider(directory);
            var logger = provider.CreateLogger("Prumo.Api.Test");
            logger.LogInformation("Informational message");
            logger.LogWarning("Warning message");
            logger.LogError(new InvalidOperationException("Provider failed"), "Error message");
            provider.Dispose(); // Drain accepted log entries before inspecting the file.
            var file = Assert.Single(Directory.GetFiles(directory, "api-*.txt"));
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
        Assert.Equal(Path.Combine(Path.GetDirectoryName(storageRoot)!, "logs", "api"), ApiFileLoggerProvider.ResolveDirectory(storageRoot));
    }
}
