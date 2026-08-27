using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class PortfolioConfiguration : IEntityTypeConfiguration<Portfolio>
{
    public void Configure(EntityTypeBuilder<Portfolio> builder)
    {
        builder.HasKey(portfolio => portfolio.Id);
        builder.Property(portfolio => portfolio.Name).HasMaxLength(120).IsRequired();
        builder.HasMany(portfolio => portfolio.Positions)
            .WithOne()
            .HasForeignKey(position => position.PortfolioId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
