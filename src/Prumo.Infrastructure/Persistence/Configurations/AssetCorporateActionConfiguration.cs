using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class AssetCorporateActionConfiguration : IEntityTypeConfiguration<AssetCorporateAction>
{
    public void Configure(EntityTypeBuilder<AssetCorporateAction> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.FactorNumerator).HasPrecision(20, 8);
        b.Property(x => x.FactorDenominator).HasPrecision(20, 8);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.SourceReference).HasMaxLength(500);
        b.HasIndex(x => new { x.AssetId, x.EffectiveDate });
        b.HasIndex(x => new { x.AssetId, x.Type, x.EffectiveDate, x.FactorNumerator, x.FactorDenominator, x.Source }).IsUnique();
        b.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
    }
}
