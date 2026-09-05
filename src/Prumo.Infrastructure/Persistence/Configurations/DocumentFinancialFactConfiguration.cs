using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class DocumentFinancialFactConfiguration : IEntityTypeConfiguration<DocumentFinancialFact>
{
    public void Configure(EntityTypeBuilder<DocumentFinancialFact> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Value).HasPrecision(24, 8);
        builder.Property(x => x.Currency).HasMaxLength(3);
        builder.Property(x => x.Unit).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Evidence).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Confidence).HasPrecision(5, 4);
        builder.HasIndex(x => new { x.DocumentId, x.AssetId, x.Metric, x.ReferencePeriod, x.PeriodType }).IsUnique();
        builder.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
    }
}
