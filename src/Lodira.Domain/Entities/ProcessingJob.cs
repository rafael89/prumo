using Lodira.Domain.Enums;

namespace Lodira.Domain.Entities;

public sealed class ProcessingJob
{
    private ProcessingJob()
    {
    }

    public ProcessingJob(Guid documentId, ProcessingJobType jobType, DateTimeOffset availableAt)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("O documento é obrigatório.", nameof(documentId));

        Id = Guid.NewGuid();
        DocumentId = documentId;
        JobType = jobType;
        Status = ProcessingJobStatus.Queued;
        AvailableAt = availableAt.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public ProcessingJobType JobType { get; private set; }
    public ProcessingJobStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset AvailableAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Document? Document { get; private set; }

    public void Start(DateTimeOffset startedAt)
    {
        if (Status != ProcessingJobStatus.Queued && Status != ProcessingJobStatus.Processing)
        {
            throw new InvalidOperationException("Somente jobs enfileirados ou expirados podem iniciar.");
        }

        Status = ProcessingJobStatus.Processing;
        Attempts++;
        StartedAt = startedAt;
        LastError = null;
    }

    public void Complete(DateTimeOffset completedAt)
    {
        if (Status != ProcessingJobStatus.Processing)
        {
            throw new InvalidOperationException("Somente jobs em processamento podem ser concluídos.");
        }

        Status = ProcessingJobStatus.Completed;
        CompletedAt = completedAt;
    }

    public void MarkFailedPermanently(DateTimeOffset failedAt, string error)
    {
        Status = ProcessingJobStatus.Failed;
        CompletedAt = failedAt;
        StartedAt = null;
        LastError = string.IsNullOrWhiteSpace(error) ? "Limite de tentativas excedido." : error.Trim();
    }

    public bool Fail(DateTimeOffset failedAt, string error, int maxAttempts, TimeSpan retryDelay)
    {
        if (Status != ProcessingJobStatus.Processing)
        {
            throw new InvalidOperationException("Somente jobs em processamento podem falhar.");
        }

        LastError = string.IsNullOrWhiteSpace(error) ? "Erro não identificado." : error.Trim();
        StartedAt = null;
        if (Attempts >= maxAttempts)
        {
            Status = ProcessingJobStatus.Failed;
            CompletedAt = failedAt;
            return false;
        }

        Status = ProcessingJobStatus.Queued;
        AvailableAt = failedAt.Add(retryDelay);
        return true;
    }
}
