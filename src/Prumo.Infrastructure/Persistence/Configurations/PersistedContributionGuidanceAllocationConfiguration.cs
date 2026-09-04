using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class PersistedContributionGuidanceAllocationConfiguration : IEntityTypeConfiguration<PersistedContributionGuidanceAllocation>
{
    public void Configure(EntityTypeBuilder<PersistedContributionGuidanceAllocation> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.Amount).HasPrecision(20, 2); b.Property(x => x.Confidence).HasPrecision(5, 4);
        b.Property(x => x.Reason).HasMaxLength(4000).IsRequired(); b.Property(x => x.EvidenceJson).HasMaxLength(100000).IsRequired();
        b.Property(x => x.CandidateKey).HasMaxLength(500);
        b.Property(x => x.Origin).HasMaxLength(40).IsRequired();
        b.Property(x => x.Provider).HasMaxLength(100); b.Property(x => x.Ticker).HasMaxLength(50); b.Property(x => x.Name).HasMaxLength(300);
        b.Property(x => x.Exchange).HasMaxLength(100); b.Property(x => x.Currency).HasMaxLength(20); b.Property(x => x.Sector).HasMaxLength(200); b.Property(x => x.SourceReference).HasMaxLength(1000);
        b.HasIndex(x => new { x.ContributionGuidanceId, x.AssetId }).IsUnique().HasFilter("\"AssetId\" IS NOT NULL");
        b.HasIndex(x => new { x.ContributionGuidanceId, x.CandidateKey }).IsUnique().HasFilter("\"CandidateKey\" IS NOT NULL");
        b.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
    }
}
