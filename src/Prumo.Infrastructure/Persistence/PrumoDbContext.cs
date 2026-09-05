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
    public DbSet<AssetPrice> AssetPrices => Set<AssetPrice>();
    public DbSet<AssetGeographicExposure> AssetGeographicExposures => Set<AssetGeographicExposure>();
    public DbSet<AssetCurrencyExposure> AssetCurrencyExposures => Set<AssetCurrencyExposure>();
    public DbSet<AssetFundamentalSnapshot> AssetFundamentalSnapshots => Set<AssetFundamentalSnapshot>();
    public DbSet<AssetValuationSnapshot> AssetValuationSnapshots => Set<AssetValuationSnapshot>();
    public DbSet<DocumentFinancialFact> DocumentFinancialFacts => Set<DocumentFinancialFact>();
    public DbSet<ContributionGuidanceJob> ContributionGuidanceJobs => Set<ContributionGuidanceJob>();
    public DbSet<PortfolioHistoricalAnchor> PortfolioHistoricalAnchors => Set<PortfolioHistoricalAnchor>();
    public DbSet<PortfolioHistoricalAnchorItem> PortfolioHistoricalAnchorItems => Set<PortfolioHistoricalAnchorItem>();
    public DbSet<FinancialHistoryCoverage> FinancialHistoryCoverages => Set<FinancialHistoryCoverage>();
    public DbSet<TransactionReconciliation> TransactionReconciliations => Set<TransactionReconciliation>();
    public DbSet<TransactionReconciliationMember> TransactionReconciliationMembers => Set<TransactionReconciliationMember>();
    public DbSet<AssetCorporateAction> AssetCorporateActions => Set<AssetCorporateAction>();
    public DbSet<PortfolioCashAnchor> PortfolioCashAnchors => Set<PortfolioCashAnchor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PrumoDbContext).Assembly);
    }
}
