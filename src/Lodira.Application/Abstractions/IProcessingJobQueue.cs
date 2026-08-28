using Lodira.Domain.Entities;
using Lodira.Domain.Enums;

namespace Lodira.Application.Abstractions;

public interface IProcessingJobQueue
{
    Task EnqueueAsync(ProcessingJob job, CancellationToken cancellationToken = default);
    Task<ProcessingJob?> ReserveNextAsync(DateTimeOffset now, TimeSpan lease, int maxAttempts, ProcessingJobType jobType, CancellationToken cancellationToken = default);
}
