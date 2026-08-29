using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Lodira.Domain.Entities;

namespace Lodira.Infrastructure.Persistence.Configurations;

public sealed class PortfolioAssessmentConfiguration : IEntityTypeConfiguration<PortfolioAssessment>
{
    public void Configure(EntityTypeBuilder<PortfolioAssessment> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Summary).HasMaxLength(4000).IsRequired(); b.Property(x => x.WhyItMatters).HasMaxLength(4000).IsRequired(); b.Property(x => x.SuggestedBehavior).HasMaxLength(2000).IsRequired();
        b.Property(x => x.AssessmentVersion).HasMaxLength(100).IsRequired();
        b.Property(x => x.PortfolioSnapshotKey).HasMaxLength(64);
        b.Property(x => x.StrategyVersionKey).HasDefaultValue(0).IsRequired();
        b.Property(x => x.ObjectiveVersionKey).HasDefaultValue(0).IsRequired();
        b.HasIndex(x => new { x.PortfolioId, x.InvestmentEventId, x.PortfolioSnapshotKey, x.StrategyVersionKey, x.ObjectiveVersionKey, x.AssessmentVersion }).IsUnique().HasFilter("\"PortfolioSnapshotKey\" IS NOT NULL");
        b.HasOne<Portfolio>().WithMany().HasForeignKey(x => x.PortfolioId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.InvestmentEvent).WithMany().HasForeignKey(x => x.InvestmentEventId).OnDelete(DeleteBehavior.Cascade);
    }
}
