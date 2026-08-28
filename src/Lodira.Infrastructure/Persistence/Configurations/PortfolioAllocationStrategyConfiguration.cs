using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Lodira.Domain.Entities;

namespace Lodira.Infrastructure.Persistence.Configurations;
public sealed class PortfolioAllocationStrategyConfiguration : IEntityTypeConfiguration<PortfolioAllocationStrategy>
{
    public void Configure(EntityTypeBuilder<PortfolioAllocationStrategy> b) { b.HasKey(x => x.Id); b.HasIndex(x => x.PortfolioId).IsUnique(); b.HasMany(x => x.Allocations).WithOne().HasForeignKey(x => x.StrategyId).OnDelete(DeleteBehavior.Cascade); }
}
public sealed class StrategyAllocationConfiguration : IEntityTypeConfiguration<StrategyAllocation>
{
    public void Configure(EntityTypeBuilder<StrategyAllocation> b) { b.HasKey(x => x.Id); b.Property(x => x.TargetPercentage).HasPrecision(20, 8); b.HasIndex(x => new { x.StrategyId, x.AssetClass }).IsUnique(); }
}
