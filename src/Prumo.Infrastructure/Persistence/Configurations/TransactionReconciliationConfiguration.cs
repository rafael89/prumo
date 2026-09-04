using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class TransactionReconciliationConfiguration : IEntityTypeConfiguration<TransactionReconciliation>
{
    public void Configure(EntityTypeBuilder<TransactionReconciliation> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => new { x.PortfolioId, x.Status });
        b.HasOne<Portfolio>().WithMany().HasForeignKey(x => x.PortfolioId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Members).WithOne(x => x.Reconciliation).HasForeignKey(x => x.ReconciliationId).OnDelete(DeleteBehavior.Cascade);
    }
}
