using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.HasKey(document => document.Id);
        builder.Property(document => document.SourceName).HasMaxLength(80).IsRequired();
        builder.Property(document => document.SourceIdentifier).HasMaxLength(200);
        builder.Property(document => document.SourceCategory).HasMaxLength(200);
        builder.Property(document => document.SourceType).HasMaxLength(200);
        builder.Property(document => document.SourceSpecies).HasMaxLength(200);
        builder.Property(document => document.SourceUrl).HasMaxLength(2000).IsRequired();
        builder.Property(document => document.DownloadUrl).HasMaxLength(2000);
        builder.Property(document => document.FilePath).HasMaxLength(1000);
        builder.Property(document => document.FileHash).HasMaxLength(128);
        builder.Property(document => document.IgnoredReason).HasMaxLength(500);
        builder.Property(document => document.Fingerprint).HasMaxLength(128).IsRequired();
        builder.HasIndex(document => document.Fingerprint).IsUnique();
        builder.HasIndex(document => new { document.SourceName, document.SourceIdentifier });
        builder.HasIndex(document => new { document.SourceName, document.SourceUrl });
        builder.HasIndex(document => new { document.AssetId, document.PublishedAt });
        builder.HasOne(document => document.Asset)
            .WithMany()
            .HasForeignKey(document => document.AssetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
