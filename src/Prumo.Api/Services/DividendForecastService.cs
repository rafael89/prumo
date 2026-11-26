using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Prumo.Application.Abstractions;
using Prumo.Application.Documents;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.Persistence;

namespace Prumo.Api.Services;

public sealed class DividendForecastService(PrumoDbContext db, IFileStorage? storage = null, IPdfTextExtractor? extractor = null, IMemoryCache? cache = null)
{
    public async Task<DividendForecastResponse?> GetAsync(Guid portfolioId, CancellationToken ct, DateOnly? selectedMonth = null)
    {
        if (!await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct)) return null;
        var now = DateTimeOffset.UtcNow;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var referenceMonth = selectedMonth is { } month
            ? new DateOnly(month.Year, month.Month, 1)
            : new DateOnly(localNow.Year, localNow.Month, 1);
        var startDateTime = referenceMonth.ToDateTime(TimeOnly.MinValue);
        var endDateTime = referenceMonth.AddMonths(1).ToDateTime(TimeOnly.MinValue);
        // Preserve the Sao Paulo month boundaries while sending UTC parameters to PostgreSQL.
        var monthStart = new DateTimeOffset(startDateTime, timeZone.GetUtcOffset(startDateTime)).ToUniversalTime();
        var monthEnd = new DateTimeOffset(endDateTime, timeZone.GetUtcOffset(endDateTime)).ToUniversalTime();
        var positions = await db.PortfolioPositions.AsNoTracking().Include(x => x.Asset)
            .Where(x => x.PortfolioId == portfolioId).ToListAsync(ct);
        var distributionPositions = positions.Where(x => x.Asset?.AssetClass is AssetClass.Fii or AssetClass.BrazilianStock)
            .ToDictionary(x => x.AssetId);
        var distributionAssetIds = distributionPositions.Keys.ToArray();
        var stockIds = distributionPositions.Values.Where(x => x.Asset!.AssetClass == AssetClass.BrazilianStock).Select(x => x.AssetId).ToHashSet();
        var quantityHistory = (await db.PortfolioPositionQuantities.AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId && distributionAssetIds.Contains(x.AssetId) && x.RecordedAt < monthEnd)
            .OrderByDescending(x => x.RecordedAt).ThenByDescending(x => x.Id).ToListAsync(ct))
            .ToLookup(x => x.AssetId);
        decimal QuantityOn(PortfolioPosition position, DateOnly? baseDate)
            => baseDate is { } date
                ? quantityHistory[position.AssetId].FirstOrDefault(x =>
                    DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.RecordedAt, timeZone).DateTime) <= date)?.Quantity ?? position.Quantity
                : position.Quantity;
        var flows = await db.PortfolioCashFlows.AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId && x.Type == PortfolioCashFlowType.Income
                && x.OccurredAt >= monthStart && x.OccurredAt < monthEnd && x.OccurredAt <= now)
            .ToListAsync(ct);
        var events = distributionAssetIds.Length == 0 ? [] : await db.InvestmentEvents.AsNoTracking()
            .AsSplitQuery().Include(x => x.AffectedAssets).Include(x => x.DocumentAnalysis!).ThenInclude(x => x.Document)
            .Where(x => x.CreatedAt <= now && x.DocumentAnalysis!.Document!.PublishedAt <= now
                && x.AffectedAssets.Any(a => stockIds.Contains(a.Id)
                    || (x.EventType == InvestmentEventType.Distribution && distributionAssetIds.Contains(a.Id)))
                && !db.DocumentAnalyses.Any(other => other.DocumentId == x.DocumentAnalysis!.DocumentId && other.CreatedAt > x.DocumentAnalysis.CreatedAt))
            .ToListAsync(ct);
        var calculated = DividendForecastCalculator.Calculate(positions, events, now, referenceMonth, QuantityOn);
        var items = calculated.GroupBy(x => x.AssetId).ToDictionary(x => x.Key, x => x.First());
        var stockItems = calculated.Where(x => stockIds.Contains(x.AssetId) && x.PaymentDate is not null).ToList();
        if (distributionAssetIds.Length > 0)
        {
            var documents = await db.Documents.AsNoTracking()
                .Where(x => distributionAssetIds.Contains(x.AssetId) && x.PublishedAt <= now)
                .OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.DiscoveredAt).ThenByDescending(x => x.Id).ToListAsync(ct);
            documents = documents.Where(x => DocumentClassificationPolicy.MayReportDistribution(
                distributionPositions[x.AssetId].Asset!.AssetClass, x.DocumentType, x.SourceCategory, x.SourceType, x.SourceSpecies)).ToList();
            var noticeIds = documents.Select(x => x.Id).ToArray();
            var noticeSummaries = noticeIds.Length == 0 ? new Dictionary<Guid, string?>() : await db.Documents.AsNoTracking()
                .Where(x => noticeIds.Contains(x.Id))
                .Select(x => new { x.Id, Summary = db.DocumentAnalyses.Where(a => a.DocumentId == x.Id)
                    .OrderByDescending(a => a.CreatedAt).Select(a => a.Summary).FirstOrDefault() })
                .ToDictionaryAsync(x => x.Id, x => x.Summary, ct);
            var eventsByAsset = events.SelectMany(x => x.AffectedAssets.Select(a => (a.Id, Event: x)))
                .ToLookup(x => x.Id, x => x.Event);
            var documentItems = new ConcurrentDictionary<Guid, IReadOnlyList<DividendForecastItem>>();
            // Each asset keeps publication order while independent document reads run concurrently.
            await Parallel.ForEachAsync(documents.GroupBy(x => x.AssetId),
                new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 4 }, async (assetDocuments, token) =>
            {
                var position = distributionPositions[assetDocuments.Key];
                var ticker = position.Asset!.Ticker;
                if (position.Asset.AssetClass == AssetClass.Fii)
                {
                    foreach (var document in assetDocuments)
                    {
                        var summary = noticeSummaries.GetValueOrDefault(document.Id);
                        var cacheKey = (document.Id, document.FileHash, ticker, summary);
                        var notice = await ReadDocumentAsync(document, cacheKey,
                            text => FiiDistributionNoticeParser.Parse(text ?? "", ticker, summary), token);
                        if (notice is null || !SameMonth(notice.PaymentDate, referenceMonth)) continue;
                        var quantity = QuantityOn(position, notice.BaseDate);
                        documentItems[position.AssetId] = [quantity > 0
                            ? new DividendForecastItem(document.AssetId, ticker,
                                decimal.Round(notice.AmountPerUnit * quantity, 2), "Aviso aos Cotistas",
                                document.SourceUrl, document.PublishedAt, notice.PaymentDate)
                            : new DividendForecastItem(document.AssetId, ticker, null, "Sem posição na data-base", null, null)];
                        break;
                    }
                    return;
                }

                var evidence = DividendForecastCalculator.ReadStockEvidence(eventsByAsset[position.AssetId], now, ticker).ToList();
                foreach (var document in assetDocuments)
                {
                    var isNotice = DocumentClassificationPolicy.IsShareholderNotice(document.SourceCategory, document.SourceType, document.SourceSpecies);
                    var cacheKey = (document.Id, document.FileHash, ticker, isNotice);
                    var original = await ReadDocumentAsync(document, cacheKey,
                        text => text is null ? [] : DividendForecastCalculator.ParseStockNotices(text, ticker, isNotice), token);
                    if (original.Count > 0)
                        evidence.RemoveAll(x => x.Document.Id == document.Id);
                    else if (!evidence.Any(x => x.Document.Id == document.Id) && noticeSummaries.GetValueOrDefault(document.Id) is { } summary)
                        original = DividendForecastCalculator.ParseStockNotices(summary, ticker, isNotice);
                    evidence.AddRange(DividendForecastCalculator.ToStockEvidence(original, document, isNotice));
                }
                documentItems[position.AssetId] = DividendForecastCalculator.BuildStockItems(position, evidence, referenceMonth, QuantityOn);
            });

            foreach (var (assetId, payments) in documentItems)
            {
                if (stockIds.Contains(assetId))
                {
                    stockItems.RemoveAll(x => x.AssetId == assetId);
                    stockItems.AddRange(payments);
                }
                items[assetId] = payments.FirstOrDefault() ?? new DividendForecastItem(assetId, distributionPositions[assetId].Asset!.Ticker,
                    null, "Sem pagamento informado neste mês", null, null);
            }
        }
        foreach (var position in positions.Where(x => x.Asset is not null))
        {
            var item = items[position.AssetId];
            if (item.PaymentDate is { } paymentDate && SameMonth(paymentDate, referenceMonth)) continue;
            var received = flows.Where(x => x.AssetId == position.AssetId)
                .Select(x => (Flow: x, LocalDate: DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(x.OccurredAt, "America/Sao_Paulo").DateTime)))
                .Where(x => SameMonth(x.LocalDate, referenceMonth)).ToList();
            items[position.AssetId] = received.Count > 0
                ? new DividendForecastItem(position.AssetId, position.Asset!.Ticker, received.Sum(x => x.Flow.Amount), "Rendimento registrado", null,
                    received.Max(x => x.Flow.OccurredAt), received.Max(x => x.LocalDate))
                : new DividendForecastItem(position.AssetId, position.Asset!.Ticker, null,
                    position.Quantity <= 0 ? "Sem posição atual" : "Sem pagamento informado neste mês", null, null);
        }
        var stockPaymentIds = stockItems.Select(x => x.AssetId).ToHashSet();
        var ordered = items.Values.Where(x => !stockPaymentIds.Contains(x.AssetId)).Concat(stockItems).OrderBy(x => x.PaymentDate is null).ThenBy(x => x.PaymentDate)
            .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.AssetId).ToList();
        return new DividendForecastResponse(decimal.Round(ordered.Sum(x => x.EstimatedMonthlyAmount ?? 0), 2), ordered, now, referenceMonth);
    }

    private static bool SameMonth(DateOnly date, DateOnly referenceMonth)
        => date.Year == referenceMonth.Year && date.Month == referenceMonth.Month;

    private async Task<T> ReadDocumentAsync<T>(Document document, object cacheKey, Func<string?, T> parse, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (storage is null || extractor is null || string.IsNullOrWhiteSpace(document.FilePath)) return parse(null);
        if (cache is not null && cache.TryGetValue(cacheKey, out T? cached)) return cached!;
        try
        {
            await using var stream = await storage.OpenReadAsync(document.FilePath, ct);
            var extracted = await extractor.ExtractAsync(stream, ct);
            var result = parse(extracted.Succeeded ? extracted.Text : null);
            cache?.Set(cacheKey, result, TimeSpan.FromHours(4));
            return result;
        }
        catch (IOException) { return parse(null); }
    }
}

public sealed record DividendForecastComponent(string Type, decimal AmountPerShare, decimal EstimatedAmount, string AmountBasis, decimal? GrossAmountPerShare, string DocumentUrl, DateTimeOffset ReferenceDate);
public sealed record DividendForecastItem(Guid AssetId, string Ticker, decimal? EstimatedMonthlyAmount, string Source, string? DocumentUrl, DateTimeOffset? ReferenceDate, DateOnly? PaymentDate = null, IReadOnlyList<DividendForecastComponent>? Components = null);
public sealed record DividendForecastResponse(decimal EstimatedMonthlyAmount, IReadOnlyList<DividendForecastItem> Items, DateTimeOffset GeneratedAt, DateOnly ReferenceMonth);
public sealed record StockDistributionEvidence(StockDividendNotice Notice, Document Document, string Source);

public static partial class DividendForecastCalculator
{
    private static readonly DateOnly UpdatedJcpWithholdingStart = new(2026, 1, 1);

    // Require an explicit BRL amount per share. Totals, yields and vague amounts are ignored.
    [GeneratedRegex(@"(?:R\$\s*([0-9]{1,4}(?:\.[0-9]{3})*,[0-9]{1,4})\s*(?:por|/)\s*(?:cota|ação|acao))|(?:(?:por|/)\s*(?:cota|ação|acao)\s*(?:de|:)?\s*R\$\s*([0-9]{1,4}(?:\.[0-9]{3})*,[0-9]{1,4}))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PerUnitAmount();

    [GeneratedRegex(@"\b(?:data\s+(?:de|do)\s+pagamento|pagamento|pagos?)(?:\s+(?:será|sera)\s+(?:realizado|efetuado))?\s*(?::|em|no\s+dia|no|para|previsto\s+para)?\s*(\d{1,2}[/-]\d{1,2}[/-]\d{4})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PaymentDatePattern();

    public static IReadOnlyList<DividendForecastItem> Calculate(IEnumerable<PortfolioPosition> positions, IEnumerable<InvestmentEvent> events, DateTimeOffset now,
        DateOnly referenceMonth, Func<PortfolioPosition, DateOnly?, decimal>? quantityOn = null)
    {
        var recentEvents = events.Where(x => x.CreatedAt <= now)
            .SelectMany(x => x.AffectedAssets.Select(asset => (asset.Id, Event: x)))
            .Where(x => x.Event.DocumentAnalysis?.Document is { } document && document.PublishedAt <= now)
            .GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.Select(y => y.Event).OrderByDescending(y => y.DocumentAnalysis!.Document!.PublishedAt).ThenByDescending(y => y.CreatedAt).ToList());
        var result = new List<DividendForecastItem>();
        foreach (var position in positions.Where(x => x.Asset is not null))
        {
            // For FIIs, the latest explicit distribution per quota is the best available monthly reference.
            // A document can announce a value without establishing the investor's entitlement to it.
            if (position.Asset!.AssetClass == AssetClass.Fii && recentEvents.TryGetValue(position.AssetId, out var assetEvents)
                && assetEvents.Any(x => x.EventType == InvestmentEventType.Distribution))
            {
                foreach (var group in assetEvents.Where(x => x.EventType == InvestmentEventType.Distribution).GroupBy(x => x.DocumentAnalysis!.DocumentId))
                {
                    var parsed = group.Select(TryReadPerUnitAmount).ToList();
                    var amounts = parsed.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
                    var paymentDates = group.Select(TryReadPaymentDate).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
                    if (!parsed.All(x => x.HasValue) || amounts.Count != 1 || paymentDates.Count != 1
                        || paymentDates[0].Year != referenceMonth.Year || paymentDates[0].Month != referenceMonth.Month) continue;
                    var baseDate = FiiDistributionNoticeParser.ReadAnalysisBaseDate(string.Join("\n", group.SelectMany(x => x.Facts.Append(x.Summary))));
                    var quantity = quantityOn?.Invoke(position, baseDate) ?? position.Quantity;
                    if (quantity <= 0) break;
                    var document = group.First().DocumentAnalysis!.Document!;
                    result.Add(new(position.AssetId, position.Asset.Ticker, decimal.Round(amounts[0] * quantity, 2),
                        "Documento analisado", document.SourceUrl, document.PublishedAt, paymentDates[0]));
                    break;
                }
                if (result.Any(x => x.AssetId == position.AssetId)) continue;
            }

            if (position.Asset.AssetClass == AssetClass.BrazilianStock && recentEvents.TryGetValue(position.AssetId, out var stockEvents))
            {
                var payments = BuildStockItems(position, ReadStockEvidence(stockEvents, now, position.Asset.Ticker), referenceMonth, quantityOn);
                if (payments.Count > 0)
                {
                    result.AddRange(payments);
                    continue;
                }
            }

            result.Add(new(position.AssetId, position.Asset.Ticker, null, "Sem pagamento informado neste mês", null, null));
        }
        return result.OrderBy(x => x.PaymentDate is null).ThenBy(x => x.PaymentDate)
            .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.AssetId).ToList();
    }

    public static IEnumerable<StockDistributionEvidence> ReadStockEvidence(IEnumerable<InvestmentEvent> events, DateTimeOffset now, string ticker)
        => events.Where(x => x.CreatedAt <= now && x.DocumentAnalysis?.Document is { } document && document.PublishedAt <= now
                && (x.EventType == InvestmentEventType.Distribution
                    || DocumentClassificationPolicy.IsShareholderNotice(document.SourceCategory, document.SourceType, document.SourceSpecies))
                && DocumentClassificationPolicy.MayReportDistribution(AssetClass.BrazilianStock, document.DocumentType,
                    document.SourceCategory, document.SourceType, document.SourceSpecies))
            .GroupBy(x => x.DocumentAnalysis!.DocumentId)
            .SelectMany(group => ParseStockEvidence(string.Join("\n", group.SelectMany(x => x.Facts.Append(x.Summary)).Distinct()),
                group.First().DocumentAnalysis!.Document!, ticker));

    internal static IReadOnlyList<StockDistributionEvidence> ParseStockEvidence(string text, Document document, string ticker)
    {
        var isNotice = DocumentClassificationPolicy.IsShareholderNotice(document.SourceCategory, document.SourceType, document.SourceSpecies);
        return ToStockEvidence(ParseStockNotices(text, ticker, isNotice), document, isNotice).ToArray();
    }

    internal static IReadOnlyList<StockDividendNotice> ParseStockNotices(string text, string ticker, bool isNotice)
        => isNotice ? StockDividendNoticeParser.ParseAll(text, ticker) : StockDividendNoticeParser.ParseInstallments(text, ticker);

    internal static IEnumerable<StockDistributionEvidence> ToStockEvidence(IEnumerable<StockDividendNotice> notices, Document document, bool isNotice)
        => notices.Select(notice => new StockDistributionEvidence(notice, document, isNotice ? "Aviso aos Acionistas" : "Fato Relevante"));

    public static IReadOnlyList<DividendForecastItem> BuildStockItems(PortfolioPosition position, IEnumerable<StockDistributionEvidence> evidence, DateOnly referenceMonth,
        Func<PortfolioPosition, DateOnly?, decimal>? quantityOn = null)
    {
        if (position.Asset is null) return [];
        // Publication recency resolves repeated or revised announcements of the same payment.
        var unique = evidence.OrderByDescending(x => x.Document.PublishedAt).ThenByDescending(x => x.Document.DiscoveredAt).ThenByDescending(x => x.Document.Id)
            .GroupBy(x => (x.Notice.Type, x.Notice.PaymentDate)).SelectMany(group => {
                var bases = group.Where(x => x.Notice.BaseDate.HasValue).Select(x => x.Notice.BaseDate!.Value).Distinct().ToList();
                // A summary omitting the base date must not add a second copy of an identified payment.
                var candidates = bases.Count > 1 ? group.Where(x => x.Notice.BaseDate.HasValue) : group;
                return candidates.GroupBy(x => x.Notice.BaseDate ?? (bases.Count == 1 ? bases[0] : (DateOnly?)null))
                    .Select(payments => (Evidence: payments.First(), BaseDate: payments.Key));
            })
            .Where(x => x.Evidence.Notice.PaymentDate.Year == referenceMonth.Year && x.Evidence.Notice.PaymentDate.Month == referenceMonth.Month)
            .Select(x => (x.Evidence, Quantity: quantityOn?.Invoke(position, x.BaseDate) ?? position.Quantity))
            .Where(x => x.Quantity > 0);
        return unique.GroupBy(x => x.Evidence.Notice.PaymentDate).OrderBy(x => x.Key).Select(group => {
            var entries = group.OrderBy(x => x.Evidence.Notice.Type).ToList();
            var first = entries[0].Evidence;
            var components = entries.Select(x => BuildStockComponent(x.Evidence, x.Quantity)).ToList();
            var source = entries.All(x => x.Evidence.Notice.Type == StockDistributionType.Dividend) ? first.Source
                : first.Source + " · " + string.Join(" + ", components.Select(x => x.Type + (x.AmountBasis switch
                {
                    nameof(StockDistributionAmountBasis.EstimatedNet) => " (líquido estimado)",
                    nameof(StockDistributionAmountBasis.Net) => " (líquido)",
                    nameof(StockDistributionAmountBasis.Gross) => " (bruto)",
                    _ => ""
                })).Distinct());
            return new DividendForecastItem(position.AssetId, position.Asset.Ticker, components.Sum(x => x.EstimatedAmount), source,
                first.Document.SourceUrl, first.Document.PublishedAt, group.Key, components);
        }).ToList();
    }

    private static DividendForecastComponent BuildStockComponent(StockDistributionEvidence evidence, decimal quantity)
    {
        var notice = evidence.Notice;
        var amount = notice.AmountPerShare;
        var basis = notice.AmountBasis;
        // Estimate statutory withholding only when the issuer confirms it and
        // the announcement, entitlement and payment are all in the new regime.
        if (notice.Type == StockDistributionType.Jcp && notice.WithholdingApplies && basis == StockDistributionAmountBasis.Gross
            && notice.BaseDate is { } baseDate && baseDate >= UpdatedJcpWithholdingStart
            && notice.PaymentDate >= UpdatedJcpWithholdingStart
            && DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(evidence.Document.PublishedAt, "America/Sao_Paulo").DateTime) >= UpdatedJcpWithholdingStart)
        {
            amount *= 0.825m;
            basis = StockDistributionAmountBasis.EstimatedNet;
        }
        return new(notice.Type == StockDistributionType.Jcp ? "JCP" : "Dividendos", amount, decimal.Round(amount * quantity, 2),
            basis.ToString(), notice.GrossAmountPerShare, evidence.Document.SourceUrl, evidence.Document.PublishedAt);
    }

    private static decimal? TryReadPerUnitAmount(InvestmentEvent item)
    {
        var values = new HashSet<decimal>();
        foreach (var fact in item.Facts.Append(item.Summary))
        {
            foreach (Match match in PerUnitAmount().Matches(fact))
            {
                var text = (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).Replace(".", "");
                if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"), out var value) && value > 0 && value <= 100)
                    values.Add(value);
            }
        }
        return values.Count == 1 ? values.Single() : null;
    }

    private static DateOnly? TryReadPaymentDate(InvestmentEvent item)
    {
        var dates = new HashSet<DateOnly>();
        foreach (var fact in item.Facts.Append(item.Summary))
        {
            foreach (Match match in PaymentDatePattern().Matches(fact))
            {
                if (DateOnly.TryParseExact(match.Groups[1].Value, new[] { "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    dates.Add(date);
            }
        }
        return dates.Count == 1 ? dates.Single() : null;
    }
}
