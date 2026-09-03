using Prumo.Api;
using Microsoft.Extensions.Logging;

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

            var file = Assert.Single(Directory.GetFiles(directory, "api-*.txt"));
            var contents = File.ReadAllText(file);
            Assert.DoesNotContain("Informational message", contents);
            Assert.Contains("Warning message", contents);
            Assert.Contains("Error message", contents);
            Assert.Contains("InvalidOperationException", contents);
            Assert.Contains("Provider failed", contents);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ResolvesLogDirectoryBesideDocuments()
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), "prumo-storage", "documents");
        var expected = Path.Combine(Path.GetDirectoryName(storageRoot)!, "logs", "api");

        Assert.Equal(Path.GetFullPath(expected), ApiFileLoggerProvider.ResolveDirectory(storageRoot));
    }
}
