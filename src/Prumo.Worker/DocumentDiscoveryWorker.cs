using Microsoft.Extensions.Options;

namespace Prumo.Worker;

public sealed class DocumentDiscoveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<DocumentDiscoveryWorker> logger;
    private readonly DocumentDiscoveryOptions options;

    public DocumentDiscoveryWorker(IServiceScopeFactory scopeFactory, IOptions<DocumentDiscoveryOptions> options, ILogger<DocumentDiscoveryWorker> logger)
    {
        this.scopeFactory = scopeFactory;
        this.options = options.Value;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.BackfillFrom.HasValue || options.BackfillTo.HasValue)
        {
            if (!options.BackfillFrom.HasValue || !options.BackfillTo.HasValue)
                throw new InvalidOperationException("DocumentDiscovery:BackfillFrom and BackfillTo must be provided together.");

            using var backfillScope = scopeFactory.CreateScope();
            await backfillScope.ServiceProvider.GetRequiredService<DocumentDiscoveryService>()
                .DiscoverAsync(options.BackfillFrom.Value, options.BackfillTo.Value, stoppingToken);

            if (options.BackfillOnly) return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<DocumentDiscoveryService>()
                    .DiscoverAsync(now.AddDays(-Math.Max(1, options.LookbackDays)), now, stoppingToken, includeRecentDistributions: true);
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Erro inesperado no ciclo de descoberta; nova tentativa em {RetryDelay}.", TimeSpan.FromMinutes(1));
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}

public sealed class DocumentDiscoveryOptions
{
    public int LookbackDays { get; set; } = 7;
    public DateTimeOffset? BackfillFrom { get; set; }
    public DateTimeOffset? BackfillTo { get; set; }
    public bool BackfillOnly { get; set; }
}
