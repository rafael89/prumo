using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class DocumentAnalysisConfiguration : IEntityTypeConfiguration<DocumentAnalysis>
{
    public void Configure(EntityTypeBuilder<DocumentAnalysis> builder)
    {
        builder.HasKey(analysis => analysis.Id);
        builder.Property(analysis => analysis.ProcessingJobId).IsRequired();
        builder.Property(analysis => analysis.Model).HasMaxLength(120).IsRequired();
        builder.Property(analysis => analysis.PromptVersion).HasMaxLength(80).IsRequired();
        builder.Property(analysis => analysis.Summary).HasMaxLength(4000).IsRequired();
        builder.Property(analysis => analysis.Impact).HasMaxLength(4000).IsRequired();
        builder.Property(analysis => analysis.PortfolioImpact).HasMaxLength(4000).IsRequired();
        builder.Property(analysis => analysis.Confidence).HasPrecision(5, 4);
        builder.Property(analysis => analysis.RawResponse).HasMaxLength(100000).IsRequired();
        builder.Property(analysis => analysis.AffectedAssets)
            .HasConversion(
                assets => JsonSerializer.Serialize(assets, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>())
            .HasColumnType("jsonb");
        builder.HasIndex(analysis => analysis.ProcessingJobId).IsUnique();
        builder.HasIndex(analysis => new { analysis.DocumentId, analysis.CreatedAt });
        builder.HasOne(analysis => analysis.Document)
            .WithMany()
            .HasForeignKey(analysis => analysis.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
