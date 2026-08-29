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
    public DbSet<TargetAllocation> TargetAllocations => Set<TargetAllocation>();
    public DbSet<PortfolioImportSession> PortfolioImportSessions => Set<PortfolioImportSession>();
    public DbSet<InvestmentEvent> InvestmentEvents => Set<InvestmentEvent>();
    public DbSet<InvestmentEventEvidence> InvestmentEventEvidence => Set<InvestmentEventEvidence>();
    public DbSet<PortfolioAssessment> PortfolioAssessments => Set<PortfolioAssessment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LodiraDbContext).Assembly);
    }
}
