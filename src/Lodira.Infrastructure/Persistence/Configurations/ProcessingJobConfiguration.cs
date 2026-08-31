using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Lodira.Domain.Entities;

namespace Lodira.Infrastructure.Persistence.Configurations;

public sealed class ProcessingJobConfiguration : IEntityTypeConfiguration<ProcessingJob>
{
    public void Configure(EntityTypeBuilder<ProcessingJob> builder)
    {
        builder.HasKey(job => job.Id);
        builder.Property(job => job.LastError).HasMaxLength(4000);
        builder.HasIndex(job => new { job.Status, job.AvailableAt });
        builder.HasIndex(job => new { job.DocumentId, job.JobType })
            .IsUnique()
            .HasFilter("\"Status\" IN (0, 1)");
        builder.HasOne(job => job.Document)
            .WithMany()
            .HasForeignKey(job => job.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
