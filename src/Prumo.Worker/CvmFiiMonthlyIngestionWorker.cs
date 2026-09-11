using Microsoft.Extensions.Options;
using Prumo.Infrastructure.MarketData;

namespace Prumo.Worker;

public sealed class CvmFiiMonthlyIngestionWorker(IServiceScopeFactory scopeFactory, ILogger<CvmFiiMonthlyIngestionWorker> logger, IOptions<CvmFiiIngestionOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<CvmFiiMonthlyIngestionService>().IngestAsync(stoppingToken);
                await Task.Delay(options.Value.Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha na ingestão estruturada de FII da CVM; nova tentativa no próximo ciclo.");
                await Task.Delay(options.Value.FailureDelay, stoppingToken);
            }
        }
    }
}

public sealed class CvmFiiIngestionOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);
    public TimeSpan FailureDelay { get; set; } = TimeSpan.FromMinutes(15);
}
