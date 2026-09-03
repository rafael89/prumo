using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Prumo.Infrastructure.Notifications;

public sealed class DiscordOptions
{
    public bool Enabled { get; set; }
    public string WebhookUrl { get; set; } = string.Empty;
    public string MinimumLevel { get; set; } = "Warning";
    public int MaxMessageLength { get; set; } = 1900;
    public LogLevel ParsedMinimumLevel => Enum.TryParse<LogLevel>(MinimumLevel, true, out var level) ? level : LogLevel.Warning;
}

public sealed record DiscordLogNotification(string Service, LogLevel Level, string Category, EventId EventId, string Message, Exception? Exception, DateTimeOffset Timestamp);
public interface IDiscordLogNotifier { void Enqueue(DiscordLogNotification notification); }

public sealed class DiscordLogNotificationService(IHttpClientFactory factory, IOptions<DiscordOptions> configured) : BackgroundService, IDiscordLogNotifier
{
    private readonly DiscordOptions options = configured.Value;
    private readonly Channel<DiscordLogNotification> queue = Channel.CreateBounded<DiscordLogNotification>(new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });

    public void Enqueue(DiscordLogNotification notification)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.WebhookUrl) || notification.Level < options.ParsedMinimumLevel) return;
        queue.Writer.TryWrite(notification);
    }

    public async Task SendAsync(DiscordLogNotification notification, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.WebhookUrl)) return;
        var payload = new { embeds = new[] { new
        {
            title = notification.Service + " · " + notification.Level.ToString().ToUpperInvariant(),
            description = FormatContent(notification, Math.Clamp(options.MaxMessageLength, 100, 1900)),
            color = GetColor(notification.Level), timestamp = notification.Timestamp.ToUniversalTime()
        }}};
        using var response = await factory.CreateClient("DiscordWebhook").PostAsJsonAsync(options.WebhookUrl, payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var notification in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try { await SendAsync(notification, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { Console.Error.WriteLine("Falha ao enviar notificação para o Discord: " + exception.Message); }
        }
    }

    public static string FormatContent(DiscordLogNotification notification, int maxLength)
    {
        var tick = ((char)96).ToString();
        var fence = tick + tick + tick;
        var exceptionText = notification.Exception is null ? string.Empty : "\n\n" + fence + "text\n" + notification.Exception.GetType().Name + ": " + notification.Exception.Message + "\n" + fence;
        var content = tick + notification.Category + tick + "\n<t:" + notification.Timestamp.ToUniversalTime().ToUnixTimeSeconds() + ":F>\n\n" + notification.Message + exceptionText;
        return content.Length <= maxLength ? content : content[..Math.Max(0, maxLength - 24)] + "\n[conteúdo truncado]";
    }

    private static int GetColor(LogLevel level) => level switch
    {
        LogLevel.Critical => 0x992D22, LogLevel.Error => 0xE74C3C, LogLevel.Warning => 0xF1C40F,
        LogLevel.Information => 0x2ECC71, LogLevel.Debug => 0x3498DB, LogLevel.Trace => 0x95A5A6, _ => 0x7F8C8D
    };
}

public sealed class DiscordLogLoggerProvider(IDiscordLogNotifier notifier, IOptions<DiscordOptions> options, string service) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new DiscordLogger(notifier, options, service, categoryName);
    public void Dispose() { }

    private sealed class DiscordLogger(IDiscordLogNotifier notifier, IOptions<DiscordOptions> options, string service, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel level) => options.Value.Enabled && !string.IsNullOrWhiteSpace(options.Value.WebhookUrl) && level >= options.Value.ParsedMinimumLevel;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(level)) notifier.Enqueue(new DiscordLogNotification(service, level, category, id, formatter(state, exception), exception, DateTimeOffset.UtcNow));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
