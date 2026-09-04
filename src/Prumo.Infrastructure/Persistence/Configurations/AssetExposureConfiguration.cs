using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class AssetGeographicExposureConfiguration : IEntityTypeConfiguration<AssetGeographicExposure>
{
    public void Configure(EntityTypeBuilder<AssetGeographicExposure> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
        builder.Property(x => x.Percentage).HasPrecision(5, 2);
        builder.Property(x => x.SourceReference).HasMaxLength(500);
        builder.HasIndex(x => new { x.AssetId, x.CountryCode }).IsUnique();
        builder.HasOne(x => x.Asset).WithMany(x => x.GeographicExposures).HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AssetCurrencyExposureConfiguration : IEntityTypeConfiguration<AssetCurrencyExposure>
{
    public void Configure(EntityTypeBuilder<AssetCurrencyExposure> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Percentage).HasPrecision(5, 2);
        builder.Property(x => x.SourceReference).HasMaxLength(500);
        builder.HasIndex(x => new { x.AssetId, x.CurrencyCode }).IsUnique();
        builder.HasOne(x => x.Asset).WithMany(x => x.CurrencyExposures).HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
    }
}
