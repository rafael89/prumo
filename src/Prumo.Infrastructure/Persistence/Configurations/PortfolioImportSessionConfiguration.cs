using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;
public sealed class PortfolioImportSessionConfiguration : IEntityTypeConfiguration<PortfolioImportSession>
{
    public void Configure(EntityTypeBuilder<PortfolioImportSession> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired(); b.Property(x => x.FileHash).HasMaxLength(64).IsRequired(); b.Property(x => x.NormalizedJson).HasMaxLength(5_000_000).IsRequired(); b.Property(x => x.Error).HasMaxLength(1000); b.Property(x => x.PortfolioSnapshotKey).HasMaxLength(64); b.HasIndex(x => new { x.PortfolioId, x.Source, x.FileHash }).IsUnique();
    }
}
