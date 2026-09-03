using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.HasKey(asset => asset.Id);
        builder.Property(asset => asset.Ticker).HasMaxLength(12).IsRequired();
        builder.Property(asset => asset.Name).HasMaxLength(200).IsRequired();
        builder.Property(asset => asset.Sector).HasMaxLength(100);
        builder.Property(asset => asset.Cnpj).HasMaxLength(14);
        builder.HasIndex(asset => asset.Cnpj);
        builder.Property(asset => asset.AssetClass).IsRequired();
        builder.HasIndex(asset => asset.Ticker).IsUnique();
    }
}
