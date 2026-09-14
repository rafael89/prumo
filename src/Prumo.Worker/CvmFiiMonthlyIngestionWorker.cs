using System.Diagnostics;
using Microsoft.Extensions.Options;
using Prumo.Infrastructure.MarketData;
using Prumo.Infrastructure.DocumentSources;

namespace Prumo.Worker;

public sealed class CvmFiiMonthlyIngestionWorker(IServiceScopeFactory scopeFactory, ILogger<CvmFiiMonthlyIngestionWorker> logger, IOptions<CvmFiiIngestionOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                logger.LogInformation("CVM FII ingestion execution started");
                using var scope = scopeFactory.CreateScope();
                var enrichment = await scope.ServiceProvider.GetRequiredService<CvmFiiCnpjEnrichmentService>().EnrichAsync(stoppingToken);
                var source = scope.ServiceProvider.GetRequiredService<CvmFiiMonthlyStructuredSource>();
                var ingestion = await scope.ServiceProvider.GetRequiredService<CvmFiiMonthlyIngestionService>().IngestAsync(stoppingToken);
                logger.LogInformation("CVM FII ingestion execution completed: year={Year}, sourceUrl={SourceUrl}, eligibleAssets={EligibleAssets}, assetsWithCnpj={AssetsWithCnpj}, rawRows={RawRows}, matchedAssets={MatchedAssets}, observationsInserted={Inserted}, observationsSkipped={Skipped}, enrichedAssets={EnrichedAssets}, conflicts={Conflicts}, normalizationFailures={NormalizationFailures}, durationMs={DurationMs}", DateTimeOffset.UtcNow.Year, source.Diagnostics.Url, ingestion.EligibleAssets, ingestion.AssetsWithCnpj, source.Diagnostics.RawRows, ingestion.MatchedAssets, ingestion.ObservationsInserted, ingestion.ObservationsSkipped, enrichment.EnrichedAssets, enrichment.Conflicts, enrichment.NormalizationFailures, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                await Task.Delay(options.Value.Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "CVM FII ingestion execution failed: durationMs={DurationMs}; retrying next cycle", Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
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
