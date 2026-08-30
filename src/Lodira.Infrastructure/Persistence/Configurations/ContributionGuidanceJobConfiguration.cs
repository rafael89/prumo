using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Lodira.Domain.Entities;
namespace Lodira.Infrastructure.Persistence.Configurations;
public sealed class ContributionGuidanceJobConfiguration : IEntityTypeConfiguration<ContributionGuidanceJob>
{
    public void Configure(EntityTypeBuilder<ContributionGuidanceJob> b)
    { b.HasKey(x => x.Id); b.Property(x => x.CandidatesJson).HasMaxLength(200000).IsRequired(); b.Property(x => x.LastError).HasMaxLength(4000); b.HasIndex(x => new { x.PortfolioId, x.ContributionGuidanceId, x.Status }); }
}
