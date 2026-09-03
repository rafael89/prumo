using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class PortfolioTransactionConfiguration : IEntityTypeConfiguration<PortfolioTransaction>
{
    public void Configure(EntityTypeBuilder<PortfolioTransaction> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.TotalAmount).HasPrecision(20, 2); b.Property(x => x.ImportFingerprint).HasMaxLength(64);
        b.HasIndex(x => new { x.PortfolioId, x.ImportFingerprint }).IsUnique().HasFilter("\"ImportFingerprint\" IS NOT NULL");
        b.HasIndex(x => new { x.PortfolioId, x.OccurredAt });
        b.HasMany(x => x.Items).WithOne(x => x.PortfolioTransaction).HasForeignKey(x => x.PortfolioTransactionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Portfolio>().WithMany().HasForeignKey(x => x.PortfolioId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<PersistedContributionGuidance>().WithMany().HasForeignKey(x => x.ContributionGuidanceId).OnDelete(DeleteBehavior.SetNull);
    }
}
