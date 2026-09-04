using Microsoft.EntityFrameworkCore;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Infrastructure.Reconciliation;

public sealed class TransactionReconciliationService(PrumoDbContext db)
{
    private const decimal PriceToleranceRate = 0.001m;
    private const decimal MinimumPriceTolerance = 0.01m;
    private const int ApproximateDateWindowDays = 31;

    public async Task<IReadOnlyList<TransactionCandidateDto>> Candidates(Guid portfolioId, CancellationToken ct)
    {
        var transactions = await LoadTransactions(portfolioId, ct);
        var resolved = await db.TransactionReconciliationMembers.AsNoTracking().Include(x => x.Reconciliation).Where(x => x.Reconciliation!.PortfolioId == portfolioId).ToListAsync(ct);
        var candidates = new List<TransactionCandidateDto>();
        for (var left = 0; left < transactions.Count; left++)
        for (var right = left + 1; right < transactions.Count; right++)
        {
            var match = Match(transactions[left], transactions[right]);
            if (match is null) continue;
            var existing = resolved.FirstOrDefault(x => x.PortfolioTransactionId == transactions[left].Id && resolved.Any(y => y.ReconciliationId == x.ReconciliationId && y.PortfolioTransactionId == transactions[right].Id));
            if (existing is not null) continue;
            candidates.Add(match);
        }
        return candidates;
    }

    public async Task<IReadOnlyList<ReconciliationDto>> List(Guid portfolioId, CancellationToken ct)
    {
        var groups = await db.TransactionReconciliations.AsNoTracking().Include(x => x.Members).ThenInclude(x => x.PortfolioTransaction!).ThenInclude(x => x.Items).Where(x => x.PortfolioId == portfolioId).OrderByDescending(x => x.UpdatedAt).ToListAsync(ct);
        var assets = await db.Assets.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        return groups.Select(x => Map(x, assets)).ToList();
    }

    public async Task<ReconciliationDto> Resolve(Guid portfolioId, ReconciliationRequest request, CancellationToken ct)
    {
        if (request.TransactionIds is null || request.TransactionIds.Distinct().Count() < 2) throw new ArgumentException("Informe ao menos duas transações diferentes.");
        var ids = request.TransactionIds.Distinct().ToHashSet();
        var transactions = await LoadTransactions(portfolioId, ct);
        var selected = transactions.Where(x => ids.Contains(x.Id)).ToList();
        if (selected.Count != ids.Count) throw new KeyNotFoundException("Todas as transações devem pertencer à carteira.");
        if (request.Status == TransactionReconciliationStatus.ConfirmedSameEconomicTransaction)
        {
            if (selected.Any(x => x.Items.Count != 1) || selected.Select(x => x.Items[0].AssetId).Distinct().Count() != 1 || selected.Select(x => x.Type).Distinct().Count() != 1 || selected.Select(x => x.Items[0].Quantity).Distinct().Count() != 1)
                throw new ArgumentException("Somente transações da mesma operação podem ser marcadas como iguais.");
            if (selected.Any(x => x.Items[0].UnitPrice <= 0)) throw new ArgumentException("A transação possui preço inválido.");
            var sameGroups = await db.TransactionReconciliations.Include(x => x.Members).Where(x => x.PortfolioId == portfolioId && x.Status == TransactionReconciliationStatus.ConfirmedSameEconomicTransaction && x.Members.Any(m => ids.Contains(m.PortfolioTransactionId))).ToListAsync(ct);
            if (sameGroups.Any(x => x.Members.Select(m => m.PortfolioTransactionId).Intersect(ids).Any() && x.Members.Select(m => m.PortfolioTransactionId).Except(ids).Any())) ids.UnionWith(sameGroups.SelectMany(x => x.Members).Select(x => x.PortfolioTransactionId));
            var distinctConflict = await db.TransactionReconciliations.AsNoTracking().Where(x => x.PortfolioId == portfolioId && x.Status == TransactionReconciliationStatus.ConfirmedDistinct && x.Members.Count(m => ids.Contains(m.PortfolioTransactionId)) > 0).AnyAsync(ct);
            if (distinctConflict) throw new InvalidOperationException("Existe uma resolução distinta conflitante.");
            var target = sameGroups.FirstOrDefault();
            if (target is null) { target = new TransactionReconciliation(portfolioId, request.Status, request.Notes); db.TransactionReconciliations.Add(target); }
            else { target.Update(request.Status, request.Notes); db.TransactionReconciliationMembers.RemoveRange(sameGroups.SelectMany(x => x.Members)); db.TransactionReconciliations.RemoveRange(sameGroups.Skip(1)); }
            foreach (var id in ids) target.Members.Add(new TransactionReconciliationMember(target.Id, id));
            await db.SaveChangesAsync(ct);
            return (await List(portfolioId, ct)).Single(x => x.Id == target.Id);
        }
        if (selected.Count != 2) throw new ArgumentException("Operações distintas devem conter exatamente duas transações.");
        var conflictingSame = await db.TransactionReconciliations.AsNoTracking().Where(x => x.PortfolioId == portfolioId && x.Status == TransactionReconciliationStatus.ConfirmedSameEconomicTransaction && x.Members.Any(m => ids.Contains(m.PortfolioTransactionId))).AnyAsync(ct);
        if (conflictingSame) throw new InvalidOperationException("Uma das transações já pertence a uma operação confirmada como igual.");
        var existingDistinct = await db.TransactionReconciliations.Include(x => x.Members).SingleOrDefaultAsync(x => x.PortfolioId == portfolioId && x.Status == TransactionReconciliationStatus.ConfirmedDistinct && x.Members.Count == 2 && x.Members.All(m => ids.Contains(m.PortfolioTransactionId)), ct);
        if (existingDistinct is null) { existingDistinct = new TransactionReconciliation(portfolioId, request.Status, request.Notes); db.TransactionReconciliations.Add(existingDistinct); foreach (var id in ids) existingDistinct.Members.Add(new TransactionReconciliationMember(existingDistinct.Id, id)); }
        else existingDistinct.Update(request.Status, request.Notes);
        await db.SaveChangesAsync(ct); return (await List(portfolioId, ct)).Single(x => x.Id == existingDistinct.Id);
    }

    public async Task<bool> Delete(Guid portfolioId, Guid reconciliationId, CancellationToken ct)
    {
        var item = await db.TransactionReconciliations.SingleOrDefaultAsync(x => x.Id == reconciliationId && x.PortfolioId == portfolioId, ct);
        if (item is null) return false; db.TransactionReconciliations.Remove(item); await db.SaveChangesAsync(ct); return true;
    }

    public async Task<IReadOnlyList<CanonicalTransactionDto>> Canonical(Guid portfolioId, CancellationToken ct)
    {
        var transactions = await LoadTransactions(portfolioId, ct);
        var groups = await db.TransactionReconciliations.AsNoTracking().Include(x => x.Members).Where(x => x.PortfolioId == portfolioId && x.Status == TransactionReconciliationStatus.ConfirmedSameEconomicTransaction).ToListAsync(ct);
        var byId = groups.SelectMany(g => g.Members.Select(m => (m.PortfolioTransactionId, Group: g))).ToDictionary(x => x.PortfolioTransactionId, x => x.Group);
        var emitted = new HashSet<Guid>(); var result = new List<CanonicalTransactionDto>();
        foreach (var transaction in transactions.OrderBy(x => x.OccurredAt).ThenBy(x => x.Id))
        {
            if (!byId.TryGetValue(transaction.Id, out var group)) { result.Add(MapCanonical(transaction, [transaction])); continue; }
            if (!emitted.Add(group.Id)) continue;
            var records = transactions.Where(x => group.Members.Any(m => m.PortfolioTransactionId == x.Id)).ToList();
            result.Add(MapCanonical(ChooseRepresentative(records), records));
        }
        return result;
    }

    private async Task<List<PortfolioTransaction>> LoadTransactions(Guid portfolioId, CancellationToken ct)
        => await db.PortfolioTransactions.AsNoTracking().Include(x => x.Items).Where(x => x.PortfolioId == portfolioId).ToListAsync(ct);

    private static TransactionCandidateDto? Match(PortfolioTransaction left, PortfolioTransaction right)
    {
        if (left.Source == right.Source || left.Items.Count != 1 || right.Items.Count != 1 || left.Type != right.Type) return null;
        var a = left.Items[0]; var b = right.Items[0];
        if (a.AssetId != b.AssetId || a.Quantity != b.Quantity) return null;
        var priceDifference = Math.Abs(a.UnitPrice - b.UnitPrice); var tolerance = Math.Max(MinimumPriceTolerance, Math.Max(a.UnitPrice, b.UnitPrice) * PriceToleranceRate);
        if (priceDifference > tolerance || !DatesCompatible(left, right)) return null;
        var exactDate = left.OccurredAt.Date == right.OccurredAt.Date;
        var strength = exactDate && priceDifference == 0 ? "Exact" : exactDate ? "Strong" : "Possible";
        var reasons = new List<string> { "SameAsset", "SameOperationType", "QuantityMatch", priceDifference == 0 ? "PriceMatch" : $"PriceDifference:{priceDifference:0.####}", exactDate ? "DateMatch" : "DatesCompatible", $"SourcePair:{left.Source}/{right.Source}" };
        return new(left.Id, right.Id, strength, reasons, Summary(left), Summary(right));
    }

    private static bool DatesCompatible(PortfolioTransaction left, PortfolioTransaction right)
    {
        if (left.OccurredAt.Date == right.OccurredAt.Date) return true;
        if (left.DatePrecision == FinancialDatePrecision.Exact && right.DatePrecision == FinancialDatePrecision.Exact) return false;
        return Math.Abs((left.OccurredAt.Date - right.OccurredAt.Date).Days) <= ApproximateDateWindowDays;
    }

    private static TransactionCandidateTransactionDto Summary(PortfolioTransaction x) => new(x.Id, x.Type.ToString(), x.OccurredAt, x.DatePrecision.ToString(), x.Items[0].AssetId, x.Items[0].Quantity, x.Items[0].UnitPrice, x.Source.ToString());
    private static PortfolioTransaction ChooseRepresentative(IReadOnlyList<PortfolioTransaction> records)
        => records.OrderBy(x => x.DatePrecision).ThenByDescending(SourceRank).ThenBy(x => x.OccurredAt).ThenBy(x => x.Id).First();
    private static int SourceRank(PortfolioTransaction x) => x.Source == FinancialSource.B3Import ? 3 : x.Source == FinancialSource.ContributionGuidanceExecution ? 2 : x.Source == FinancialSource.Manual ? 1 : 0;
    private static ReconciliationDto Map(TransactionReconciliation x, IReadOnlyDictionary<Guid, Asset> assets) => new(x.Id, x.Status.ToString(), x.Notes, x.CreatedAt, x.UpdatedAt, x.Members.Select(m => new ReconciliationMemberDto(m.PortfolioTransactionId, m.PortfolioTransaction?.Type.ToString() ?? "", m.PortfolioTransaction?.OccurredAt, m.PortfolioTransaction?.Items.FirstOrDefault() is { } i ? assets.GetValueOrDefault(i.AssetId)?.Ticker : null)).ToList());
    private static CanonicalTransactionDto MapCanonical(PortfolioTransaction representative, IReadOnlyList<PortfolioTransaction> records)
    {
        var priceRecord = records.OrderByDescending(SourceRank).ThenBy(x => x.Id).First();
        var items = representative.Items.Select(i => new CanonicalTransactionItemDto(i.AssetId, i.Quantity, priceRecord.Items.FirstOrDefault(x => x.AssetId == i.AssetId)?.UnitPrice ?? i.UnitPrice)).ToList();
        return new(representative.Id, representative.Type.ToString(), representative.OccurredAt, representative.DatePrecision.ToString(), priceRecord.TotalAmount, items, records.Select(x => x.Id).ToList(), records.Select(x => x.Source.ToString()).Distinct().OrderBy(x => x).ToList(), records.Any(x => x.OccurredAt != representative.OccurredAt || x.Items.Any(i => representative.Items.All(r => r.AssetId != i.AssetId || r.UnitPrice != i.UnitPrice || r.Quantity != i.Quantity))), records.Any(x => x.ContributionGuidanceId.HasValue));
    }
}

public sealed record ReconciliationRequest(IReadOnlyList<Guid> TransactionIds, TransactionReconciliationStatus Status, string? Notes = null);
public sealed record TransactionCandidateDto(Guid TransactionAId, Guid TransactionBId, string MatchStrength, IReadOnlyList<string> Reasons, TransactionCandidateTransactionDto TransactionA, TransactionCandidateTransactionDto TransactionB);
public sealed record TransactionCandidateTransactionDto(Guid Id, string Type, DateTimeOffset OccurredAt, string DatePrecision, Guid AssetId, decimal Quantity, decimal UnitPrice, string Source);
public sealed record ReconciliationDto(Guid Id, string Status, string? Notes, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<ReconciliationMemberDto> Members);
public sealed record ReconciliationMemberDto(Guid TransactionId, string Type, DateTimeOffset? OccurredAt, string? Ticker);
public sealed record CanonicalTransactionDto(Guid Id, string Type, DateTimeOffset OccurredAt, string DatePrecision, decimal TotalAmount, IReadOnlyList<CanonicalTransactionItemDto> Items, IReadOnlyList<Guid> SourceRecordIds, IReadOnlyList<string> Sources, bool HasFieldConflict, bool HasContributionGuidance = false);
public sealed record CanonicalTransactionItemDto(Guid AssetId, decimal Quantity, decimal UnitPrice);
