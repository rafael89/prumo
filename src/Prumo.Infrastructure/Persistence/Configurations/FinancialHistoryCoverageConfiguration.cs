using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class FinancialHistoryCoverageConfiguration : IEntityTypeConfiguration<FinancialHistoryCoverage>
{
    public void Configure(EntityTypeBuilder<FinancialHistoryCoverage> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => new { x.PortfolioId, x.Dimension, x.FromDate, x.ToDate });
        b.HasIndex(x => new { x.PortfolioId, x.AssetId, x.Dimension });
        b.HasOne<Portfolio>().WithMany().HasForeignKey(x => x.PortfolioId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
    }
}
