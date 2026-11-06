using System.Text;
using System.Threading.Channels;

namespace Prumo.Infrastructure.Logging;

/// <summary>A bounded, single-consumer log queue. Disposal drains accepted entries.</summary>
public sealed class BufferedFileLogWriter : IDisposable
{
    private readonly string directory;
    private readonly string prefix;
    private readonly Channel<(DateTimeOffset Timestamp, string Text)> queue = Channel.CreateBounded<(DateTimeOffset, string)>(
        new BoundedChannelOptions(2048) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Task consumer;
    private long dropped;
    private int disposed;

    public BufferedFileLogWriter(string directory, string prefix)
    {
        this.directory = Path.GetFullPath(directory);
        this.prefix = prefix;
        consumer = Task.Run(ConsumeAsync);
    }

    public void Write(DateTimeOffset timestamp, string text)
    {
        if (Volatile.Read(ref disposed) == 0 && !queue.Writer.TryWrite((timestamp, text)))
            Interlocked.Increment(ref dropped);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        queue.Writer.TryComplete();
        consumer.GetAwaiter().GetResult();
    }

    private async Task ConsumeAsync()
    {
        var nextRetention = DateTimeOffset.MinValue;
        while (await queue.Reader.WaitToReadAsync())
        {
            var batches = new Dictionary<DateOnly, StringBuilder>();
            for (var count = 0; count < 128 && queue.Reader.TryRead(out var entry); count++)
            {
                var date = DateOnly.FromDateTime(entry.Timestamp.Date);
                if (!batches.TryGetValue(date, out var batch)) batches[date] = batch = new();
                batch.Append(entry.Text);
            }
            try
            {
                Directory.CreateDirectory(directory);
                var now = DateTimeOffset.UtcNow;
                if (now >= nextRetention)
                {
                    RemoveExpiredFiles(now.UtcDateTime);
                    nextRetention = now.AddHours(1);
                }
                var skipped = Interlocked.Exchange(ref dropped, 0);
                if (skipped > 0 && batches.Count > 0)
                    batches.First().Value.AppendLine($"{now:O} [Warning] File log queue full: {skipped} entries dropped.");
                foreach (var (date, batch) in batches)
                    await File.AppendAllTextAsync(Path.Combine(directory, $"{prefix}-{date:yyyy-MM-dd}.txt"), batch.ToString(), new UTF8Encoding(false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void RemoveExpiredFiles(DateTime nowUtc)
    {
        foreach (var path in Directory.EnumerateFiles(directory, $"{prefix}-*.txt"))
        {
            try { if (File.GetLastWriteTimeUtc(path) < nowUtc.AddDays(-7)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
