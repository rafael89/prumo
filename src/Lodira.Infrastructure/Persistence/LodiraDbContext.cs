using Microsoft.EntityFrameworkCore;
using Lodira.Domain.Entities;

namespace Lodira.Infrastructure.Persistence;

public sealed class LodiraDbContext : DbContext
{
    public LodiraDbContext(DbContextOptions<LodiraDbContext> options)
        : base(options)
    {
    }

    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Portfolio> Portfolios => Set<Portfolio>();
    public DbSet<PortfolioPosition> PortfolioPositions => Set<PortfolioPosition>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();
    public DbSet<DocumentAnalysis> DocumentAnalyses => Set<DocumentAnalysis>();
    public DbSet<RadarItemState> RadarItemStates => Set<RadarItemState>();
    public DbSet<PortfolioAllocationStrategy> PortfolioAllocationStrategies => Set<PortfolioAllocationStrategy>();
    public DbSet<StrategyAllocation> StrategyAllocations => Set<StrategyAllocation>();
    public DbSet<PortfolioImportSession> PortfolioImportSessions => Set<PortfolioImportSession>();
    public DbSet<InvestmentObjective> InvestmentObjectives => Set<InvestmentObjective>();
    public DbSet<InvestmentEvent> InvestmentEvents => Set<InvestmentEvent>();
    public DbSet<InvestmentEventEvidence> InvestmentEventEvidence => Set<InvestmentEventEvidence>();
    public DbSet<PortfolioAssessment> PortfolioAssessments => Set<PortfolioAssessment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LodiraDbContext).Assembly);
    }
}
