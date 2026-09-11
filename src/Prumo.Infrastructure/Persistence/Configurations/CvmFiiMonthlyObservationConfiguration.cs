using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class CvmFiiMonthlyObservationConfiguration : IEntityTypeConfiguration<CvmFiiMonthlyObservation>
{
    public void Configure(EntityTypeBuilder<CvmFiiMonthlyObservation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FundClassCnpj).HasMaxLength(14).IsRequired();
        builder.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceReference).HasMaxLength(500).IsRequired();
        builder.Property(x => x.SchemaVersion).HasMaxLength(80).IsRequired();
        builder.Property(x => x.NetAssetValue).HasPrecision(24, 8);
        builder.Property(x => x.NavPerShare).HasPrecision(24, 8);
        builder.Property(x => x.Cash).HasPrecision(24, 8);
        builder.HasIndex(x => x.Fingerprint).IsUnique();
        builder.HasIndex(x => new { x.AssetId, x.FundClassCnpj, x.ReferenceDate, x.ObservedAt });
        builder.HasOne(x => x.Asset).WithMany(x => x.CvmFiiMonthlyObservations).HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
    }
}
