using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prumo.Domain.Entities;

namespace Prumo.Infrastructure.Persistence.Configurations;

public sealed class AssetFundamentalSnapshotConfiguration : IEntityTypeConfiguration<AssetFundamentalSnapshot>
{
    public void Configure(EntityTypeBuilder<AssetFundamentalSnapshot> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceReference).HasMaxLength(500);
        foreach (var property in new[] { nameof(AssetFundamentalSnapshot.Revenue), nameof(AssetFundamentalSnapshot.NetIncome), nameof(AssetFundamentalSnapshot.Equity), nameof(AssetFundamentalSnapshot.TotalDebt), nameof(AssetFundamentalSnapshot.Cash), nameof(AssetFundamentalSnapshot.Ebitda), nameof(AssetFundamentalSnapshot.Roe), nameof(AssetFundamentalSnapshot.Roic), nameof(AssetFundamentalSnapshot.NetMargin) }) builder.Property<decimal?>(property).HasPrecision(20, 6);
        builder.HasIndex(x => new { x.AssetId, x.Source, x.ReferenceDate, x.PeriodType }).IsUnique();
        builder.HasOne(x => x.Asset).WithMany(x => x.FundamentalSnapshots).HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AssetValuationSnapshotConfiguration : IEntityTypeConfiguration<AssetValuationSnapshot>
{
    public void Configure(EntityTypeBuilder<AssetValuationSnapshot> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceReference).HasMaxLength(500);
        foreach (var property in new[] { nameof(AssetValuationSnapshot.PriceEarnings), nameof(AssetValuationSnapshot.PriceBook), nameof(AssetValuationSnapshot.EvEbitda), nameof(AssetValuationSnapshot.DividendYield) }) builder.Property<decimal?>(property).HasPrecision(20, 6);
        builder.HasIndex(x => new { x.AssetId, x.Source, x.ReferenceDate }).IsUnique();
        builder.HasOne(x => x.Asset).WithMany(x => x.ValuationSnapshots).HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
    }
}
