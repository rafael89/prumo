using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Lodira.Domain.Entities;

namespace Lodira.Infrastructure.Persistence.Configurations;

public sealed class InvestmentEventConfiguration : IEntityTypeConfiguration<InvestmentEvent>
{
    public void Configure(EntityTypeBuilder<InvestmentEvent> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Summary).HasMaxLength(4000).IsRequired();
        b.Property(x => x.Fingerprint).HasMaxLength(128).IsRequired();
        b.Property(x => x.Facts).HasConversion(x => JsonSerializer.Serialize(x, (JsonSerializerOptions?)null), x => JsonSerializer.Deserialize<List<string>>(x, (JsonSerializerOptions?)null) ?? new());
        b.Property(x => x.Facts).HasColumnType("jsonb");
        b.HasIndex(x => new { x.DocumentAnalysisId, x.Fingerprint }).IsUnique();
        b.HasOne(x => x.DocumentAnalysis).WithMany().HasForeignKey(x => x.DocumentAnalysisId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.AffectedAssets).WithMany().UsingEntity("InvestmentEventAssets");
    }
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
