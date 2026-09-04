using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class PortfolioCashFlowConfiguration : IEntityTypeConfiguration<PortfolioCashFlow>
{
    public void Configure(EntityTypeBuilder<PortfolioCashFlow> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Amount).HasPrecision(20, 2);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.DatePrecision).IsRequired();
        b.HasIndex(x => new { x.PortfolioId, x.OccurredAt });
        b.HasOne<Portfolio>().WithMany().HasForeignKey(x => x.PortfolioId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
    }
}
