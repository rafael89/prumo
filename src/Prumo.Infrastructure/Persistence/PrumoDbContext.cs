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
    public DbSet<PortfolioPositionQuantity> PortfolioPositionQuantities => Set<PortfolioPositionQuantity>();
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
    public DbSet<CvmFiiMonthlyObservation> CvmFiiMonthlyObservations => Set<CvmFiiMonthlyObservation>();
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
        modelBuilder.Entity<PortfolioPositionQuantity>(builder =>
        {
            builder.Property(x => x.Quantity).HasPrecision(20, 8);
            builder.HasIndex(x => new { x.PortfolioId, x.AssetId, x.RecordedAt });
            builder.HasOne<Portfolio>().WithMany().HasForeignKey(x => x.PortfolioId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RecordPositionQuantities();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RecordPositionQuantities();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void RecordPositionQuantities()
    {
        var changes = ChangeTracker.Entries<PortfolioPosition>().Where(entry =>
            entry.State is EntityState.Added or EntityState.Deleted
            || (entry.State == EntityState.Modified && entry.Property(x => x.Quantity).IsModified
                && entry.Property(x => x.Quantity).OriginalValue != entry.Entity.Quantity)).ToList();
        if (changes.Count == 0) return;

        // Reuse entries staged by a previous save attempt in the same context.
        var pending = ChangeTracker.Entries<PortfolioPositionQuantity>().Where(x => x.State == EntityState.Added)
            .Select(x => (x.Entity.PortfolioId, x.Entity.AssetId, x.Entity.Quantity)).ToHashSet();
        var deletedPortfolios = ChangeTracker.Entries<Portfolio>().Where(x => x.State == EntityState.Deleted)
            .Select(x => x.Entity.Id).ToHashSet();
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in changes)
        {
            var position = entry.Entity;
            var quantity = entry.State == EntityState.Deleted ? 0 : position.Quantity;
            if (!deletedPortfolios.Contains(position.PortfolioId) && pending.Add((position.PortfolioId, position.AssetId, quantity)))
                PortfolioPositionQuantities.Add(new(position.PortfolioId, position.AssetId, now, quantity));
        }
    }
}
