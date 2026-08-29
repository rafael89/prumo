using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Lodira.Domain.Entities;

namespace Lodira.Infrastructure.Persistence.Configurations;

public sealed class InvestmentEventConfiguration : IEntityTypeConfiguration<InvestmentEvent>
{
    private static readonly ValueComparer<List<string>> FactsComparer = new(
        (left, right) => AreEqual(left, right),
        facts => GetContentHash(facts),
        facts => Snapshot(facts));

    public void Configure(EntityTypeBuilder<InvestmentEvent> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Summary).HasMaxLength(4000).IsRequired();
        b.Property(x => x.Fingerprint).HasMaxLength(128).IsRequired();
        b.Property(x => x.Facts)
            .HasConversion(
                facts => JsonSerializer.Serialize(facts, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(FactsComparer);
        b.Property(x => x.Facts).HasColumnType("jsonb");
        b.HasIndex(x => new { x.DocumentAnalysisId, x.Fingerprint }).IsUnique();
        b.HasOne(x => x.DocumentAnalysis).WithMany().HasForeignKey(x => x.DocumentAnalysisId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.AffectedAssets).WithMany().UsingEntity("InvestmentEventAssets");
    }

    private static bool AreEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null || left.Count != right.Count) return false;
        return left.SequenceEqual(right, StringComparer.Ordinal);
    }

    private static int GetContentHash(IReadOnlyList<string>? facts)
    {
        if (facts is null) return 0;
        var hash = new HashCode();
        foreach (var fact in facts) hash.Add(fact, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    private static List<string> Snapshot(IReadOnlyList<string>? facts) => facts is null ? new List<string>() : facts.ToList();
}

public sealed class InvestmentEventEvidenceConfiguration : IEntityTypeConfiguration<InvestmentEventEvidence>
{
    public void Configure(EntityTypeBuilder<InvestmentEventEvidence> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.Reference).HasMaxLength(1000).IsRequired();
        b.HasOne<InvestmentEvent>().WithMany(x => x.Evidence).HasForeignKey(x => x.InvestmentEventId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<DocumentAnalysis>().WithMany().HasForeignKey(x => x.DocumentAnalysisId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
    }
}
