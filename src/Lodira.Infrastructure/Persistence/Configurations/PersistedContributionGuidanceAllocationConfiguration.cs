using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Lodira.Domain.Entities;

namespace Lodira.Infrastructure.Persistence.Configurations;

public sealed class PersistedContributionGuidanceAllocationConfiguration : IEntityTypeConfiguration<PersistedContributionGuidanceAllocation>
{
    public void Configure(EntityTypeBuilder<PersistedContributionGuidanceAllocation> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.Amount).HasPrecision(20, 2); b.Property(x => x.Confidence).HasPrecision(5, 4);
        b.Property(x => x.Reason).HasMaxLength(4000).IsRequired(); b.Property(x => x.EvidenceJson).HasMaxLength(100000).IsRequired();
        b.HasIndex(x => new { x.ContributionGuidanceId, x.AssetId }).IsUnique();
        b.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
    }
}
