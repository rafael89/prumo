using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Lodira.Application.Contributions;
using Lodira.Domain.Entities;
using Lodira.Infrastructure.Persistence;
using Lodira.Infrastructure.Ai;

namespace Lodira.Worker;

public sealed class ContributionGuidanceWorker(IServiceScopeFactory scopes, ILogger<ContributionGuidanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LodiraDbContext>(); var job = await db.ContributionGuidanceJobs.Where(x => x.Status == Lodira.Domain.Enums.ContributionGuidanceJobStatus.Queued).OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(stoppingToken);
            if (job is null) { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); continue; }
            job.Start(); await db.SaveChangesAsync(stoppingToken);
            try
            {
                var guidance = await db.ContributionGuidances.Include(x => x.Allocations).SingleAsync(x => x.Id == job.ContributionGuidanceId, stoppingToken);
                var portfolio = await db.Portfolios.Include(x => x.Positions).ThenInclude(x => x.Asset).SingleAsync(x => x.Id == job.PortfolioId, stoppingToken);
                var positions = JsonSerializer.Deserialize<List<GuidancePosition>>(job.CandidatesJson) ?? new(); var classBudgets = guidance.Allocations.Join(positions, a => a.AssetId, p => p.AssetId, (a, p) => new { p.AssetClass, a.Amount }).GroupBy(x => x.AssetClass).ToDictionary(x => x.Key, x => x.Sum(a => a.Amount));
                var candidates = positions.Select(x => new ContributionCandidate(x.AssetId, x.Ticker, x.AssetClass, x.CurrentValue, x.CurrentWeight, 0, 0, classBudgets.GetValueOrDefault(x.AssetClass), false, Array.Empty<ContributionAssessmentContext>())).ToList();
                var provider = scope.ServiceProvider.GetRequiredService<IContributionIntelligenceProvider>(); var result = await provider.EvaluateAsync(job.PortfolioId, guidance.RequestedAmount, candidates, stoppingToken);
                if (ContributionIntelligenceValidator.IsValid(result, guidance.RequestedAmount, candidates) && result.Availability != ContributionIntelligenceAvailability.InsufficientIntelligence)
                {
                    var applied = ContributionIntelligenceApplier.Apply(new ContributionGuidanceCalculator().Calculate(new ContributionGuidanceInput(job.PortfolioId, guidance.RequestedAmount, new Lodira.Application.Portfolio.PortfolioHealthResult(Lodira.Domain.Enums.PortfolioHealthStatus.Aligned, Array.Empty<Lodira.Application.Portfolio.AllocationDeviation>(), null, Array.Empty<Lodira.Application.Portfolio.CapitalNeed>(), "worker", DateTimeOffset.UtcNow), positions, Array.Empty<GuidanceAssessment>())), result, positions);
                    guidance.ReplaceAllocations(applied.Allocations.Select(a => new PersistedContributionGuidanceAllocation(guidance.Id, a.AssetId, a.Amount, a.Reason, 0, JsonSerializer.Serialize(a.Evidence))));
                }
                job.Complete(); await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex) { job.Fail(ex.Message); await db.SaveChangesAsync(stoppingToken); logger.LogError(ex, "Falha no job de guidance {JobId}; fallback persistido", job.Id); }
        }
    }
}
