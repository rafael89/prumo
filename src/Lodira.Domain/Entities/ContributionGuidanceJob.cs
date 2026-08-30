using Lodira.Domain.Enums;
namespace Lodira.Domain.Entities;
public sealed class ContributionGuidanceJob
{
    private ContributionGuidanceJob() { }
    public ContributionGuidanceJob(Guid portfolioId, Guid guidanceId, string candidatesJson)
    { Id = Guid.NewGuid(); PortfolioId = portfolioId; ContributionGuidanceId = guidanceId; CandidatesJson = candidatesJson; Status = ContributionGuidanceJobStatus.Queued; CreatedAt = DateTimeOffset.UtcNow; }
    public Guid Id { get; private set; } public Guid PortfolioId { get; private set; } public Guid ContributionGuidanceId { get; private set; }
    public string CandidatesJson { get; private set; } = string.Empty; public ContributionGuidanceJobStatus Status { get; private set; }
    public int Attempts { get; private set; } public string? LastError { get; private set; } public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; } public DateTimeOffset? CompletedAt { get; private set; }
    public void Start() { Status = ContributionGuidanceJobStatus.Processing; Attempts++; StartedAt = DateTimeOffset.UtcNow; LastError = null; }
    public void Complete() { Status = ContributionGuidanceJobStatus.Completed; CompletedAt = DateTimeOffset.UtcNow; }
    public void Fail(string error) { Status = ContributionGuidanceJobStatus.Failed; LastError = error; CompletedAt = DateTimeOffset.UtcNow; }
}
