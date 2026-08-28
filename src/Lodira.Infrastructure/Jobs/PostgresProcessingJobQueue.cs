using Microsoft.EntityFrameworkCore;
using Lodira.Application.Abstractions;
using Lodira.Domain.Entities;
using Lodira.Domain.Enums;
using Lodira.Infrastructure.Persistence;

namespace Lodira.Infrastructure.Jobs;

public sealed class PostgresProcessingJobQueue : IProcessingJobQueue
{
    private readonly LodiraDbContext dbContext;

    public PostgresProcessingJobQueue(LodiraDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public async Task EnqueueAsync(ProcessingJob job, CancellationToken cancellationToken = default)
    {
        dbContext.ProcessingJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProcessingJob?> ReserveNextAsync(DateTimeOffset now, TimeSpan lease, int maxAttempts, ProcessingJobType jobType, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var staleAt = now.Subtract(lease);
        var queued = (int)ProcessingJobStatus.Queued;
        var processing = (int)ProcessingJobStatus.Processing;
        var type = (int)jobType;

        var job = await dbContext.ProcessingJobs
            .FromSqlInterpolated($"""
                SELECT *
                FROM "ProcessingJobs"
                     WHERE "JobType" = {type}
                        AND (("Status" = {queued} AND "AvailableAt" <= {now})
                         OR ("Status" = {processing} AND "StartedAt" <= {staleAt}))
                ORDER BY "AvailableAt"
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        if (job.Attempts >= maxAttempts)
        {
            job.MarkFailedPermanently(now, "Limite de tentativas excedido após expiração do lease.");
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        job.Start(now);
        await dbContext.Entry(job).Reference(item => item.Document).LoadAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return job;
    }
}
