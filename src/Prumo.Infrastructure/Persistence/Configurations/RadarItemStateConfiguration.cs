using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class RadarItemStateConfiguration : IEntityTypeConfiguration<RadarItemState>
{
    public void Configure(EntityTypeBuilder<RadarItemState> builder)
    {
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => new { item.PortfolioId, item.DocumentAnalysisId }).IsUnique();
        builder.HasOne<DocumentAnalysis>().WithMany().HasForeignKey(item => item.DocumentAnalysisId).OnDelete(DeleteBehavior.Cascade);
    }
}
