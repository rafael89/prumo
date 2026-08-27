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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PrumoDbContext).Assembly);
    }
}
