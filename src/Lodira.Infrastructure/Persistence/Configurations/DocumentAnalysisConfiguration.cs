using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Lodira.Domain.Entities;

namespace Lodira.Infrastructure.Persistence.Configurations;

public sealed class DocumentAnalysisConfiguration : IEntityTypeConfiguration<DocumentAnalysis>
{
    private static readonly ValueComparer<List<string>> AffectedAssetsComparer = new(
        (left, right) => AreEqual(left, right),
        assets => GetContentHash(assets),
        assets => Snapshot(assets));

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
            .Metadata.SetValueComparer(AffectedAssetsComparer);
        builder.Property(analysis => analysis.AffectedAssets)
            .HasColumnType("jsonb");
        builder.HasIndex(analysis => analysis.ProcessingJobId).IsUnique();
        builder.HasIndex(analysis => new { analysis.DocumentId, analysis.Model, analysis.PromptVersion }).IsUnique();
        builder.HasIndex(analysis => new { analysis.DocumentId, analysis.CreatedAt });
        builder.HasOne(analysis => analysis.Document)
            .WithMany()
            .HasForeignKey(analysis => analysis.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static bool AreEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static int GetContentHash(IReadOnlyList<string>? assets)
    {
        if (assets is null)
        {
            return 0;
        }

        var hash = new HashCode();
        foreach (var asset in assets)
        {
            hash.Add(asset, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    private static List<string> Snapshot(IReadOnlyList<string>? assets)
    {
        return assets is null ? new List<string>() : assets.ToList();
    }
}
