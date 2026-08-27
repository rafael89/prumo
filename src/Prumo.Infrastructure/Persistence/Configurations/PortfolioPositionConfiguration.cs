using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class PortfolioPositionConfiguration : IEntityTypeConfiguration<PortfolioPosition>
{
    public void Configure(EntityTypeBuilder<PortfolioPosition> builder)
    {
        builder.HasKey(position => position.Id);
        builder.Property(position => position.Quantity).HasPrecision(20, 8);
        builder.Property(position => position.AveragePrice).HasPrecision(20, 8);
        builder.Property(position => position.CurrentPrice).HasPrecision(20, 8);
        builder.Property(position => position.CurrentValue).HasPrecision(20, 8);
        builder.Property(position => position.PortfolioWeight).HasPrecision(20, 8);
        builder.HasOne(position => position.Asset)
            .WithMany()
            .HasForeignKey(position => position.AssetId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(position => new { position.PortfolioId, position.AssetId }).IsUnique();
    }
}
