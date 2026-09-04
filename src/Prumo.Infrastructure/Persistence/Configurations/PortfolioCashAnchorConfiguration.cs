using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class PortfolioCashAnchorConfiguration : IEntityTypeConfiguration<PortfolioCashAnchor>
{
    public void Configure(EntityTypeBuilder<PortfolioCashAnchor> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.Balance).HasPrecision(20, 2); b.Property(x => x.Currency).HasMaxLength(3).IsRequired(); b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => new { x.PortfolioId, x.AnchorDate }).IsUnique();
        b.HasOne<Portfolio>().WithMany().HasForeignKey(x => x.PortfolioId).OnDelete(DeleteBehavior.Cascade);
    }
}
