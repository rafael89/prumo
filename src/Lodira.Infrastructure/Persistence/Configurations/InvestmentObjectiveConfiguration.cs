using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Lodira.Domain.Entities;

namespace Lodira.Infrastructure.Persistence.Configurations;

public sealed class InvestmentObjectiveConfiguration : IEntityTypeConfiguration<InvestmentObjective>
{
    public void Configure(EntityTypeBuilder<InvestmentObjective> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TargetAmount).HasPrecision(20, 8);
        builder.HasIndex(x => new { x.PortfolioId, x.Version }).IsUnique();
        builder.HasIndex(x => x.PortfolioId).HasFilter("\"ArchivedAt\" IS NULL").IsUnique();
        builder.HasOne<Portfolio>().WithMany(x => x.InvestmentObjectives).HasForeignKey(x => x.PortfolioId).OnDelete(DeleteBehavior.Cascade);
    }
}
