using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class PortfolioHistoricalAnchorItemConfiguration : IEntityTypeConfiguration<PortfolioHistoricalAnchorItem>
{
    public void Configure(EntityTypeBuilder<PortfolioHistoricalAnchorItem> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.Quantity).HasPrecision(20, 8); b.Property(x => x.AverageCost).HasPrecision(20, 8);
        b.HasIndex(x => new { x.AnchorId, x.AssetId }).IsUnique();
        b.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
    }
}
