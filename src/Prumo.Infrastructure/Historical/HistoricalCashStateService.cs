using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Reconciliation;

namespace Prumo.Infrastructure.Historical;

public sealed class HistoricalCashStateService(PrumoDbContext db, CoverageService coverage, TransactionReconciliationService reconciliation)
{
    public async Task<IReadOnlyList<CashAnchorDto>> ListAnchors(Guid portfolioId, CancellationToken ct)
        => await db.PortfolioCashAnchors.AsNoTracking().Where(x => x.PortfolioId == portfolioId).OrderByDescending(x => x.AnchorDate).Select(x => new CashAnchorDto(x.Id, x.PortfolioId, x.AnchorDate, x.Balance, x.Currency, x.Source.ToString(), x.DatePrecision.ToString(), x.Notes, x.CreatedAt, x.UpdatedAt)).ToListAsync(ct);

    public async Task<CashAnchorDto> CreateAnchor(Guid portfolioId, CashAnchorRequest request, CancellationToken ct)
    {
        if (!await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct)) throw new KeyNotFoundException("Carteira não encontrada.");
        if (request.Currency != "BRL") throw new ArgumentException("A moeda suportada é BRL.");
        if (await db.PortfolioCashAnchors.AnyAsync(x => x.PortfolioId == portfolioId && x.AnchorDate == request.AnchorDate, ct)) throw new InvalidOperationException("Já existe uma âncora de caixa nessa data.");
        var anchor = new PortfolioCashAnchor(portfolioId, request.AnchorDate, request.Balance, request.Source, request.DatePrecision, request.Currency, request.Notes); db.PortfolioCashAnchors.Add(anchor); await db.SaveChangesAsync(ct); return (await ListAnchors(portfolioId, ct)).Single(x => x.Id == anchor.Id);
    }

    public async Task<CashAnchorDto?> UpdateAnchor(Guid portfolioId, Guid id, CashAnchorRequest request, CancellationToken ct)
    {
        var anchor = await db.PortfolioCashAnchors.SingleOrDefaultAsync(x => x.PortfolioId == portfolioId && x.Id == id, ct); if (anchor is null) return null;
        if (request.Currency != "BRL") throw new ArgumentException("A moeda suportada é BRL.");
        if (await db.PortfolioCashAnchors.AnyAsync(x => x.PortfolioId == portfolioId && x.AnchorDate == request.AnchorDate && x.Id != id, ct)) throw new InvalidOperationException("Já existe uma âncora de caixa nessa data.");
        anchor.Update(request.AnchorDate, request.Balance, request.Source, request.DatePrecision, request.Currency, request.Notes); await db.SaveChangesAsync(ct); return (await ListAnchors(portfolioId, ct)).Single(x => x.Id == id);
    }

    public async Task<bool> DeleteAnchor(Guid portfolioId, Guid id, CancellationToken ct)
    {
        var anchor = await db.PortfolioCashAnchors.SingleOrDefaultAsync(x => x.PortfolioId == portfolioId && x.Id == id, ct); if (anchor is null) return false; db.PortfolioCashAnchors.Remove(anchor); await db.SaveChangesAsync(ct); return true;
    }

    public async Task<HistoricalCashStateDto?> Get(Guid portfolioId, DateOnly asOf, CancellationToken ct, Guid? anchorId = null)
    {
        if (!await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct)) return null;
        var anchorQuery = db.PortfolioCashAnchors.AsNoTracking().Where(x => x.PortfolioId == portfolioId && x.AnchorDate <= asOf);
        if (anchorId.HasValue) anchorQuery = anchorQuery.Where(x => x.Id == anchorId.Value);
        var anchor = await anchorQuery.OrderByDescending(x => x.AnchorDate).FirstOrDefaultAsync(ct);
        if (anchor is null) return new(portfolioId, asOf, null, null, "Unavailable", null, ["NoCashAnchor"], [], [], []);
        var transactions = (await reconciliation.Canonical(portfolioId, ct)).Where(x => DateOnly.FromDateTime(x.OccurredAt.UtcDateTime) > anchor.AnchorDate && DateOnly.FromDateTime(x.OccurredAt.UtcDateTime) <= asOf).ToList();
        var flows = await db.PortfolioCashFlows.AsNoTracking().Where(x => x.PortfolioId == portfolioId && DateOnly.FromDateTime(x.OccurredAt.UtcDateTime) > anchor.AnchorDate && DateOnly.FromDateTime(x.OccurredAt.UtcDateTime) <= asOf).OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).ToListAsync(ct);
        var balance = anchor.Balance; var warnings = new HashSet<string>(); var transactionIds = new List<Guid>(); var flowIds = new List<Guid>();
        if (anchor.DatePrecision != FinancialDatePrecision.Exact) warnings.Add("ApproximateCashAnchorDate");
        foreach (var date in transactions.Select(x => DateOnly.FromDateTime(x.OccurredAt.UtcDateTime)).Concat(flows.Select(x => DateOnly.FromDateTime(x.OccurredAt.UtcDateTime))).Distinct().OrderBy(x => x))
        {
            foreach (var flow in flows.Where(x => DateOnly.FromDateTime(x.OccurredAt.UtcDateTime) == date)) { if (flow.DatePrecision != FinancialDatePrecision.Exact) warnings.Add("ApproximateCashFlowDate"); balance += flow.Type == PortfolioCashFlowType.Withdrawal ? -flow.Amount : flow.Amount; flowIds.Add(flow.Id); }
            foreach (var transaction in transactions.Where(x => DateOnly.FromDateTime(x.OccurredAt.UtcDateTime) == date))
            {
                if (transaction.DatePrecision != nameof(FinancialDatePrecision.Exact)) warnings.Add("ApproximateTransactionDate");
                var amount = transaction.Items.Sum(x => x.Quantity * x.UnitPrice); balance += transaction.Type == nameof(PortfolioTransactionType.Sell) ? amount : -amount; transactionIds.AddRange(transaction.SourceRecordIds);
            }
        }
        var candidates = await reconciliation.Candidates(portfolioId, ct);
        if (candidates.Any(x => transactionIds.Contains(x.TransactionAId) || transactionIds.Contains(x.TransactionBId))) warnings.Add("UnresolvedTransactionReconciliation");
        if (balance < 0) warnings.Add("NegativeCashBalance");
        var transactionCoverage = await coverage.Resolve(portfolioId, FinancialHistoryCoverageDimension.Transactions, anchor.AnchorDate, asOf, null, ct);
        var cashFlowCoverage = await coverage.Resolve(portfolioId, FinancialHistoryCoverageDimension.CashFlows, anchor.AnchorDate, asOf, null, ct);
        var incomeCoverage = await coverage.Resolve(portfolioId, FinancialHistoryCoverageDimension.Income, anchor.AnchorDate, asOf, null, ct);
        if (transactionCoverage.Status != "Complete") warnings.Add("MissingTransactionCoverage");
        if (cashFlowCoverage.Status != "Complete") warnings.Add("MissingCashFlowCoverage");
        if (incomeCoverage.Status != "Complete") warnings.Add("MissingIncomeCoverage");
        var complete = transactionCoverage.Status == "Complete" && cashFlowCoverage.Status == "Complete" && incomeCoverage.Status == "Complete" && warnings.All(x => x is not "ApproximateCashAnchorDate" and not "ApproximateTransactionDate" and not "ApproximateCashFlowDate" and not "UnresolvedTransactionReconciliation" and not "NegativeCashBalance");
        var evidence = transactionIds.Concat(flowIds).ToList();
        return new(portfolioId, asOf, anchor.Id, balance, complete ? "Complete" : "Partial", complete ? anchor.AnchorDate : null, warnings.ToList(), evidence, transactionIds, flowIds);
    }
}

public sealed record HistoricalCashStateDto(Guid PortfolioId, DateOnly AsOfDate, Guid? AnchorId, decimal? Balance, string Coverage, DateOnly? ReliableFrom, IReadOnlyList<string> Warnings, IReadOnlyList<Guid> EvidenceIds, IReadOnlyList<Guid> AppliedTransactionIds, IReadOnlyList<Guid> AppliedCashFlowIds);
public sealed record CashAnchorRequest(DateOnly AnchorDate, decimal Balance, string Currency, FinancialSource Source, FinancialDatePrecision DatePrecision, string? Notes = null);
public sealed record CashAnchorDto(Guid Id, Guid PortfolioId, DateOnly AnchorDate, decimal Balance, string Currency, string Source, string DatePrecision, string? Notes, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
