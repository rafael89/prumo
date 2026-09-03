using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class AssetPriceConfiguration : IEntityTypeConfiguration<AssetPrice>
{
    public void Configure(EntityTypeBuilder<AssetPrice> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Close).HasPrecision(20, 8);
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.Source).HasMaxLength(80).IsRequired();
        b.HasIndex(x => new { x.AssetId, x.PriceDate, x.Source }).IsUnique();
        b.HasIndex(x => new { x.AssetId, x.PriceDate });
        b.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
    }
}
