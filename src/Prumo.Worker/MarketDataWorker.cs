using Microsoft.Extensions.DependencyInjection;
using Prumo.Infrastructure.MarketData;

namespace Prumo.Worker;

public sealed class MarketDataWorker(IServiceScopeFactory scopeFactory, ILogger<MarketDataWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<MarketDataService>();
                await service.RefreshAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromDays(3), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha no ciclo de atualização de preços; nova tentativa em 1 minuto.");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
