using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Lodira.Domain.Entities;

namespace Lodira.Infrastructure.Persistence.Configurations;

public sealed class TargetAllocationConfiguration : IEntityTypeConfiguration<TargetAllocation>
{
    public void Configure(EntityTypeBuilder<TargetAllocation> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.TargetPercentage).HasPrecision(20, 8).IsRequired();
        b.HasIndex(x => new { x.PortfolioId, x.AssetClass }).IsUnique();
        b.HasOne<Portfolio>().WithMany(x => x.TargetAllocations).HasForeignKey(x => x.PortfolioId).OnDelete(DeleteBehavior.Cascade);
    }
}
