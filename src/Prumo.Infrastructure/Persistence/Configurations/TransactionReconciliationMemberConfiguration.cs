using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class TransactionReconciliationMemberConfiguration : IEntityTypeConfiguration<TransactionReconciliationMember>
{
    public void Configure(EntityTypeBuilder<TransactionReconciliationMember> b)
    {
        b.HasKey(x => x.Id); b.HasIndex(x => new { x.ReconciliationId, x.PortfolioTransactionId }).IsUnique();
        b.HasIndex(x => x.PortfolioTransactionId);
        b.HasOne(x => x.PortfolioTransaction).WithMany().HasForeignKey(x => x.PortfolioTransactionId).OnDelete(DeleteBehavior.Restrict);
    }
}
