using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Lodira.Domain.Entities;

namespace Lodira.Infrastructure.Persistence.Configurations;
public sealed class PortfolioAllocationStrategyConfiguration : IEntityTypeConfiguration<PortfolioAllocationStrategy>
{
    public void Configure(EntityTypeBuilder<PortfolioAllocationStrategy> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.TemplateKey).HasMaxLength(80);
        b.Property(x => x.MaximumAssetWeight).HasPrecision(20, 8);
        b.HasIndex(x => new { x.PortfolioId, x.Version }).IsUnique();
        b.HasIndex(x => x.PortfolioId).HasFilter("\"ArchivedAt\" IS NULL").IsUnique();
        b.HasMany(x => x.Allocations).WithOne().HasForeignKey(x => x.StrategyId).OnDelete(DeleteBehavior.Cascade);
    }
}
public sealed class StrategyAllocationConfiguration : IEntityTypeConfiguration<StrategyAllocation>
{
    public void Configure(EntityTypeBuilder<StrategyAllocation> b) { b.HasKey(x => x.Id); b.Property(x => x.TargetPercentage).HasPrecision(20, 8); b.HasIndex(x => new { x.StrategyId, x.AssetClass }).IsUnique(); }
}
