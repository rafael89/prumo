using System.Text;

namespace Prumo.Api;

public sealed class ApiFileLoggerProvider(string directory, LogLevel minimumLevel = LogLevel.Warning) : ILoggerProvider
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private readonly object sync = new();
    private readonly string directory = Path.GetFullPath(directory);

    public ILogger CreateLogger(string categoryName) => new ApiFileLogger(this, categoryName);
    public void Dispose() { }
    internal bool IsEnabled(LogLevel level) => level >= minimumLevel;

    internal void Write(LogLevel level, string category, EventId eventId, string message, Exception? exception)
    {
        if (level < minimumLevel) return;
        var timestamp = DateTimeOffset.Now;
        var text = new StringBuilder().Append(timestamp.ToString("O")).Append(" [").Append(level).Append("] ").Append(category);
        if (eventId.Id != 0) text.Append(" [").Append(eventId.Id).Append(']');
        text.AppendLine().AppendLine(message);
        if (exception is not null) text.AppendLine(exception.ToString());
        text.AppendLine();
        try
        {
            lock (sync)
            {
                Directory.CreateDirectory(directory);
                RemoveExpiredFiles(timestamp.UtcDateTime);
                File.AppendAllText(Path.Combine(directory, $"api-{timestamp:yyyy-MM-dd}.txt"), text.ToString(), new UTF8Encoding(false));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void RemoveExpiredFiles(DateTime nowUtc)
    {
        var cutoff = nowUtc - Retention;
        foreach (var path in Directory.EnumerateFiles(directory, "api-*.txt"))
        {
            try { if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static string ResolveDirectory(string storageRoot, string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath)) return Path.GetFullPath(configuredPath);
        var root = new DirectoryInfo(Path.GetFullPath(storageRoot));
        var storageDirectory = string.Equals(root.Name, "documents", StringComparison.OrdinalIgnoreCase) ? root.Parent ?? root : root;
        return Path.Combine(storageDirectory.FullName, "logs", "api");
    }

    private sealed class ApiFileLogger(ApiFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (IsEnabled(logLevel)) provider.Write(logLevel, category, eventId, formatter(state, exception), exception); }
    }
}
