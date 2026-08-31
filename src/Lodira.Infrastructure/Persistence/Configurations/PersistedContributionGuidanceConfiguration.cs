using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Lodira.Domain.Entities;

namespace Lodira.Infrastructure.Persistence.Configurations;

public sealed class PersistedContributionGuidanceConfiguration : IEntityTypeConfiguration<PersistedContributionGuidance>
{
    public void Configure(EntityTypeBuilder<PersistedContributionGuidance> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.RequestedAmount).HasPrecision(20, 2);
        b.Property(x => x.PortfolioSnapshotKey).HasMaxLength(64).IsRequired(); b.Property(x => x.TargetAllocationKey).HasMaxLength(64).IsRequired();
        b.Property(x => x.IntelligenceContextKey).HasMaxLength(64).IsRequired();
        b.Property(x => x.EngineVersion).HasMaxLength(100).IsRequired(); b.Property(x => x.Provider).HasMaxLength(100).IsRequired(); b.Property(x => x.Model).HasMaxLength(200).IsRequired();
        b.Property(x => x.IntelligenceAvailability).HasMaxLength(50).IsRequired(); b.Property(x => x.Reasoning).HasMaxLength(10000).IsRequired(); b.Property(x => x.MetadataJson).HasMaxLength(100000).IsRequired();
        b.HasIndex(x => new { x.PortfolioId, x.PortfolioSnapshotKey, x.TargetAllocationKey, x.IntelligenceContextKey, x.RequestedAmount, x.GeneratedAt });
        b.HasMany(x => x.Allocations).WithOne(x => x.ContributionGuidance).HasForeignKey(x => x.ContributionGuidanceId).OnDelete(DeleteBehavior.Cascade);
    }
}
