using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Api.Services;

public sealed class DatabaseHealthCheck(PrumoDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("PostgreSQL indisponível.");
}
