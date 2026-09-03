using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class FinancialLedgerTests
{
    [Fact]
    public void Buy_and_sell_have_distinct_fingerprints()
    {
        var portfolioId = Guid.NewGuid(); var assetId = Guid.NewGuid(); var date = DateTimeOffset.UtcNow;
        var buy = PortfolioTransaction.CreateImportFingerprint(portfolioId, assetId, PortfolioTransactionType.Buy, date, 10, 20, "XP");
        var sell = PortfolioTransaction.CreateImportFingerprint(portfolioId, assetId, PortfolioTransactionType.Sell, date, 10, 20, "XP");
        Assert.NotEqual(buy, sell);
    }

    [Fact]
    public void Cash_flow_does_not_require_an_asset_except_for_income()
    {
        var portfolioId = Guid.NewGuid();
        var contribution = new PortfolioCashFlow(portfolioId, PortfolioCashFlowType.Contribution, 1000, DateTimeOffset.UtcNow, FinancialSource.Manual);
        var withdrawal = new PortfolioCashFlow(portfolioId, PortfolioCashFlowType.Withdrawal, 300, DateTimeOffset.UtcNow, FinancialSource.Manual);
        Assert.Null(contribution.AssetId);
        Assert.Null(withdrawal.AssetId);
        Assert.Throws<ArgumentException>(() => new PortfolioCashFlow(portfolioId, PortfolioCashFlowType.Income, 50, DateTimeOffset.UtcNow, FinancialSource.Manual));
    }

    [Fact]
    public void Existing_buy_constructor_remains_compatible()
    {
        var transaction = new PortfolioTransaction(Guid.NewGuid(), PortfolioTransactionType.Buy, DateTimeOffset.UtcNow, null, 200);
        Assert.Equal(PortfolioTransactionType.Buy, transaction.Type);
        Assert.Equal(FinancialSource.Unknown, transaction.Source);
    }
}
