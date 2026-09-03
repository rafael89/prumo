using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence;

public sealed class PrumoDbContext : DbContext
{
    public PrumoDbContext(DbContextOptions<PrumoDbContext> options)
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
    public DbSet<PersistedContributionGuidance> ContributionGuidances => Set<PersistedContributionGuidance>();
    public DbSet<PersistedContributionGuidanceAllocation> ContributionGuidanceAllocations => Set<PersistedContributionGuidanceAllocation>();
    public DbSet<PortfolioTransaction> PortfolioTransactions => Set<PortfolioTransaction>();
    public DbSet<PortfolioTransactionItem> PortfolioTransactionItems => Set<PortfolioTransactionItem>();
    public DbSet<PortfolioCashFlow> PortfolioCashFlows => Set<PortfolioCashFlow>();
    public DbSet<ContributionGuidanceJob> ContributionGuidanceJobs => Set<ContributionGuidanceJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PrumoDbContext).Assembly);
    }
}
