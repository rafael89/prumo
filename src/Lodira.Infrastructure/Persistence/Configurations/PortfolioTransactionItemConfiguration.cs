using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Lodira.Domain.Entities;

namespace Lodira.Infrastructure.Persistence.Configurations;

public sealed class PortfolioTransactionItemConfiguration : IEntityTypeConfiguration<PortfolioTransactionItem>
{
    public void Configure(EntityTypeBuilder<PortfolioTransactionItem> b)
    {
        b.HasKey(x => x.Id); b.Property(x => x.Quantity).HasPrecision(20, 8); b.Property(x => x.UnitPrice).HasPrecision(20, 8);
        b.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
    }
}
