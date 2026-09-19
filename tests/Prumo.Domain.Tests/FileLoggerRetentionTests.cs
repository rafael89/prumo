using Microsoft.Extensions.Logging;
using Prumo.Api;
using Prumo.Worker;

namespace Prumo.Domain.Tests;

public sealed class FileLoggerRetentionTests
{
    [Fact]
    public void Api_logger_removes_files_older_than_seven_days_when_writing()
        => AssertRetention(
            path => new ApiFileLoggerProvider(path),
            "api-2020-01-01.txt",
            "api-2099-01-01.txt");

    [Fact]
    public void Worker_logger_removes_files_older_than_seven_days_when_writing()
        => AssertRetention(
            path => new WorkerFileLoggerProvider(path),
            "worker-2020-01-01.txt",
            "worker-2099-01-01.txt");

    private static void AssertRetention(Func<string, ILoggerProvider> create, string oldName, string currentName)
    {
        var directory = Path.Combine(Path.GetTempPath(), "prumo-log-retention", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var oldPath = Path.Combine(directory, oldName);
            var currentPath = Path.Combine(directory, currentName);
            File.WriteAllText(oldPath, "old");
            File.SetLastWriteTimeUtc(oldPath, DateTime.UtcNow.AddDays(-8));
            using (var provider = create(directory))
                provider.CreateLogger("test").LogWarning("current");
            Assert.False(File.Exists(oldPath));
            Assert.True(File.Exists(currentPath) || Directory.EnumerateFiles(directory).Any());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
