using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class PortfolioHistoricalAnchorConfiguration : IEntityTypeConfiguration<PortfolioHistoricalAnchor>
{
    public void Configure(EntityTypeBuilder<PortfolioHistoricalAnchor> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => new { x.PortfolioId, x.AnchorDate }).IsUnique();
        b.HasOne<Portfolio>().WithMany().HasForeignKey(x => x.PortfolioId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Items).WithOne(x => x.Anchor).HasForeignKey(x => x.AnchorId).OnDelete(DeleteBehavior.Cascade);
    }
}
