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
        var assetIds = positions.Select(x => x.AssetId).ToHashSet();
        var flows = await db.PortfolioCashFlows.AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId && x.Type == PortfolioCashFlowType.Income
                && x.OccurredAt >= monthStart && x.OccurredAt < monthEnd && x.OccurredAt <= now)
            .ToListAsync(ct);
        var events = assetIds.Count == 0 ? new List<InvestmentEvent>() : await db.InvestmentEvents.AsNoTracking()
            .AsSplitQuery().Include(x => x.AffectedAssets).Include(x => x.DocumentAnalysis!).ThenInclude(x => x.Document)
            .Where(x => x.AffectedAssets.Any(a => assetIds.Contains(a.Id)) && x.CreatedAt <= now
                && !db.DocumentAnalyses.Any(other => other.DocumentId == x.DocumentAnalysis!.DocumentId && other.CreatedAt > x.DocumentAnalysis.CreatedAt))
            .ToListAsync(ct);
        var calculated = DividendForecastCalculator.Calculate(positions, events, now, referenceMonth);
        var items = calculated.GroupBy(x => x.AssetId).ToDictionary(x => x.Key, x => x.First());
        var stockIds = positions.Where(x => x.Asset?.AssetClass == AssetClass.BrazilianStock).Select(x => x.AssetId).ToHashSet();
        var stockItems = calculated.Where(x => stockIds.Contains(x.AssetId) && x.PaymentDate is not null).ToList();
        var fiiNoticeAssetIds = new HashSet<Guid>();
        if (assetIds.Count > 0)
        {
            var assetClasses = positions.Where(x => x.Asset is not null).ToDictionary(x => x.AssetId, x => x.Asset!.AssetClass);
            var documents = await db.Documents.AsNoTracking()
                .Where(x => assetIds.Contains(x.AssetId) && x.PublishedAt <= now)
                .OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.DiscoveredAt).ThenByDescending(x => x.Id).ToListAsync(ct);
            documents = documents.Where(x => assetClasses.TryGetValue(x.AssetId, out var assetClass)
                && DocumentClassificationPolicy.MayReportDistribution(assetClass, x.DocumentType, x.SourceCategory, x.SourceType, x.SourceSpecies)).ToList();
            var noticeIds = documents.Select(x => x.Id).ToArray();
            var noticeSummaries = noticeIds.Length == 0 ? new Dictionary<Guid, string>() :
                (await db.DocumentAnalyses.AsNoTracking()
                    .Where(x => noticeIds.Contains(x.DocumentId))
                    .Select(x => new { x.DocumentId, x.Summary, x.CreatedAt })
                    .ToListAsync(ct))
                .GroupBy(x => x.DocumentId)
                .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.CreatedAt).First().Summary);
            foreach (var assetDocuments in documents.GroupBy(x => x.AssetId))
            {
                var position = positions.Single(x => x.AssetId == assetDocuments.Key);
                if (position.Quantity <= 0 || position.Asset is null) continue;
                if (position.Asset.AssetClass == AssetClass.Fii)
                {
                    foreach (var document in assetDocuments)
                    {
                        var text = await ReadDocumentTextAsync(document, ct);
                        var summary = noticeSummaries.GetValueOrDefault(document.Id);
                        var notice = FiiDistributionNoticeParser.Parse(text ?? "", position.Asset.Ticker, summary);
                        if (notice is null || !SameMonth(notice.PaymentDate, referenceMonth)) continue;
                        items[document.AssetId] = new DividendForecastItem(document.AssetId, position.Asset.Ticker,
                            decimal.Round(notice.AmountPerUnit * position.Quantity, 2), "Aviso aos Cotistas",
                            document.SourceUrl, document.PublishedAt, notice.PaymentDate);
                        fiiNoticeAssetIds.Add(document.AssetId);
                        break;
                    }
                }
                else if (position.Asset.AssetClass == AssetClass.BrazilianStock)
                {
                    var evidence = DividendForecastCalculator.ReadStockEvidence(events.Where(x => x.AffectedAssets.Any(a => a.Id == position.AssetId)), now, position.Asset.Ticker).ToList();
                    foreach (var document in assetDocuments)
                    {
                        var text = await ReadDocumentTextAsync(document, ct);
                        var original = text is null ? [] : DividendForecastCalculator.ParseStockEvidence(text, document, position.Asset.Ticker);
                        if (original.Count > 0)
                            evidence.RemoveAll(x => x.Document.Id == document.Id);
                        else if (!evidence.Any(x => x.Document.Id == document.Id) && noticeSummaries.TryGetValue(document.Id, out var summary))
                            original = DividendForecastCalculator.ParseStockEvidence(summary, document, position.Asset.Ticker);
                        evidence.AddRange(original);
                    }
                    var payments = DividendForecastCalculator.BuildStockItems(position, evidence, referenceMonth);
                    stockItems.RemoveAll(x => x.AssetId == position.AssetId);
                    stockItems.AddRange(payments);
                    items[position.AssetId] = payments.FirstOrDefault() ?? new DividendForecastItem(position.AssetId, position.Asset.Ticker, null, "Sem pagamento informado neste mês", null, null);
                }
            }
        }
        foreach (var position in positions.Where(x => x.Asset is not null))
        {
            if (fiiNoticeAssetIds.Contains(position.AssetId)) continue;
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

    private async Task<string?> ReadDocumentTextAsync(Document document, CancellationToken ct)
    {
        if (storage is null || extractor is null || string.IsNullOrWhiteSpace(document.FilePath)) return null;
        var cacheKey = $"dividend-document-text:{document.Id}:{document.FileHash}";
        if (cache is not null && cache.TryGetValue(cacheKey, out string? cached)) return cached;
        try
        {
            await using var stream = await storage.OpenReadAsync(document.FilePath, ct);
            var extracted = await extractor.ExtractAsync(stream, ct);
            var text = extracted.Succeeded ? extracted.Text : null;
            cache?.Set(cacheKey, text, TimeSpan.FromHours(4));
            return text;
        }
        catch (IOException) { return null; }
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

    public static IReadOnlyList<DividendForecastItem> Calculate(IEnumerable<PortfolioPosition> positions, IEnumerable<InvestmentEvent> events, DateTimeOffset now, DateOnly referenceMonth)
    {
        var recentEvents = events.Where(x => x.CreatedAt <= now)
            .SelectMany(x => x.AffectedAssets.Select(asset => (asset.Id, Event: x)))
            .Where(x => x.Event.DocumentAnalysis?.Document is { } document && document.PublishedAt <= now)
            .GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.Select(y => y.Event).OrderByDescending(y => y.DocumentAnalysis!.Document!.PublishedAt).ThenByDescending(y => y.CreatedAt).ToList());
        var result = new List<DividendForecastItem>();
        foreach (var position in positions.Where(x => x.Asset is not null))
        {
            if (position.Quantity <= 0)
            {
                result.Add(new(position.AssetId, position.Asset!.Ticker, null, "Sem posição atual", null, null));
                continue;
            }
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
                    var document = group.First().DocumentAnalysis!.Document!;
                    result.Add(new(position.AssetId, position.Asset.Ticker, decimal.Round(amounts[0] * position.Quantity, 2),
                        "Documento analisado", document.SourceUrl, document.PublishedAt, paymentDates[0]));
                    break;
                }
                if (result.Any(x => x.AssetId == position.AssetId)) continue;
            }

            if (position.Asset.AssetClass == AssetClass.BrazilianStock && recentEvents.TryGetValue(position.AssetId, out var stockEvents))
            {
                var payments = BuildStockItems(position, ReadStockEvidence(stockEvents, now, position.Asset.Ticker), referenceMonth);
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
        var notices = isNotice ? StockDividendNoticeParser.ParseAll(text, ticker) : StockDividendNoticeParser.ParseInstallments(text, ticker);
        return notices.Select(notice => new StockDistributionEvidence(notice, document, isNotice ? "Aviso aos Acionistas" : "Fato Relevante")).ToArray();
    }

    public static IReadOnlyList<DividendForecastItem> BuildStockItems(PortfolioPosition position, IEnumerable<StockDistributionEvidence> evidence, DateOnly referenceMonth)
    {
        if (position.Quantity <= 0 || position.Asset is null) return [];
        // Publication recency resolves repeated or revised announcements of the same payment.
        var unique = evidence.OrderByDescending(x => x.Document.PublishedAt).ThenByDescending(x => x.Document.DiscoveredAt).ThenByDescending(x => x.Document.Id)
            .GroupBy(x => (x.Notice.Type, x.Notice.PaymentDate)).SelectMany(group => {
                var bases = group.Where(x => x.Notice.BaseDate.HasValue).Select(x => x.Notice.BaseDate!.Value).Distinct().ToList();
                // A summary omitting the base date must not add a second copy of an identified payment.
                var candidates = bases.Count > 1 ? group.Where(x => x.Notice.BaseDate.HasValue) : group;
                return candidates.GroupBy(x => x.Notice.BaseDate ?? (bases.Count == 1 ? bases[0] : (DateOnly?)null)).Select(payments => payments.First());
            })
            .Where(x => x.Notice.PaymentDate.Year == referenceMonth.Year && x.Notice.PaymentDate.Month == referenceMonth.Month);
        return unique.GroupBy(x => x.Notice.PaymentDate).OrderBy(x => x.Key).Select(group => {
            var entries = group.OrderBy(x => x.Notice.Type).ToList();
            var components = entries.Select(x => BuildStockComponent(x, position.Quantity)).ToList();
            var source = entries.All(x => x.Notice.Type == StockDistributionType.Dividend) ? entries[0].Source
                : entries[0].Source + " · " + string.Join(" + ", components.Select(x => x.Type + (x.AmountBasis switch
                {
                    nameof(StockDistributionAmountBasis.EstimatedNet) => " (líquido estimado)",
                    nameof(StockDistributionAmountBasis.Net) => " (líquido)",
                    nameof(StockDistributionAmountBasis.Gross) => " (bruto)",
                    _ => ""
                })).Distinct());
            return new DividendForecastItem(position.AssetId, position.Asset.Ticker, components.Sum(x => x.EstimatedAmount), source,
                entries[0].Document.SourceUrl, entries[0].Document.PublishedAt, group.Key, components);
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
