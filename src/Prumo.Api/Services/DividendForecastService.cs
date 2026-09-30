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
    public async Task<DividendForecastResponse?> GetAsync(Guid portfolioId, CancellationToken ct)
    {
        if (!await db.Portfolios.AnyAsync(x => x.Id == portfolioId, ct)) return null;
        var now = DateTimeOffset.UtcNow;
        var localNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, "America/Sao_Paulo");
        var referenceMonth = new DateOnly(localNow.Year, localNow.Month, 1);
        var positions = await db.PortfolioPositions.AsNoTracking().Include(x => x.Asset)
            .Where(x => x.PortfolioId == portfolioId).ToListAsync(ct);
        var assetIds = positions.Select(x => x.AssetId).ToHashSet();
        var since = now.AddMonths(-1);
        var flows = await db.PortfolioCashFlows.AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId && x.Type == PortfolioCashFlowType.Income && x.OccurredAt >= since && x.OccurredAt <= now)
            .ToListAsync(ct);
        var events = assetIds.Count == 0 ? new List<InvestmentEvent>() : await db.InvestmentEvents.AsNoTracking()
            .AsSplitQuery().Include(x => x.AffectedAssets).Include(x => x.DocumentAnalysis!).ThenInclude(x => x.Document)
            .Where(x => x.AffectedAssets.Any(a => assetIds.Contains(a.Id)) && x.CreatedAt >= now.AddDays(-120)
                && !db.DocumentAnalyses.Any(other => other.DocumentId == x.DocumentAnalysis!.DocumentId && other.CreatedAt > x.DocumentAnalysis.CreatedAt))
            .ToListAsync(ct);
        var items = DividendForecastCalculator.Calculate(positions, events, now, referenceMonth).ToDictionary(x => x.AssetId);
        var fiiNoticeAssetIds = new HashSet<Guid>();
        if (storage is not null && extractor is not null && assetIds.Count > 0)
        {
            var documents = await db.Documents.AsNoTracking()
                .Where(x => assetIds.Contains(x.AssetId) && x.PublishedAt >= now.AddDays(-90) && x.PublishedAt <= now)
                .OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.DiscoveredAt).ThenByDescending(x => x.Id).ToListAsync(ct);
            var fiiNoticeIds = documents.Where(x => FiiDistributionNoticeParser.IsNotice(x.SourceCategory, x.SourceType))
                .Select(x => x.Id).ToArray();
            var fiiSummaries = fiiNoticeIds.Length == 0 ? new Dictionary<Guid, string>() :
                (await db.DocumentAnalyses.AsNoTracking()
                    .Where(x => fiiNoticeIds.Contains(x.DocumentId))
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
                    foreach (var document in assetDocuments.Where(x => FiiDistributionNoticeParser.IsNotice(x.SourceCategory, x.SourceType)))
                    {
                        var text = await ReadDocumentTextAsync(document, ct);
                        var notice = text is null ? null : FiiDistributionNoticeParser.Parse(text);
                        if (notice is null && fiiSummaries.TryGetValue(document.Id, out var summary))
                            notice = FiiDistributionNoticeParser.ParseAnalysisSummary(summary);
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
                    foreach (var document in assetDocuments)
                    {
                        var text = await ReadDocumentTextAsync(document, ct);
                        var notice = text is null ? null : StockDividendNoticeParser.Parse(text);
                        if (notice is null || !SameMonth(notice.PaymentDate, referenceMonth)) continue;
                        items[document.AssetId] = new DividendForecastItem(document.AssetId, position.Asset.Ticker,
                            decimal.Round(notice.AmountPerShare * position.Quantity, 2), "Comunicado de dividendos", document.SourceUrl, document.PublishedAt, notice.PaymentDate);
                        break;
                    }
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
        var ordered = items.Values.OrderBy(x => x.PaymentDate is null).ThenBy(x => x.PaymentDate)
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

public sealed record DividendForecastItem(Guid AssetId, string Ticker, decimal? EstimatedMonthlyAmount, string Source, string? DocumentUrl, DateTimeOffset? ReferenceDate, DateOnly? PaymentDate = null);
public sealed record DividendForecastResponse(decimal EstimatedMonthlyAmount, IReadOnlyList<DividendForecastItem> Items, DateTimeOffset GeneratedAt, DateOnly ReferenceMonth);

public static partial class DividendForecastCalculator
{
    // Require an explicit BRL amount per share. Totals, yields and vague amounts are ignored.
    [GeneratedRegex(@"(?:R\$\s*([0-9]{1,4}(?:\.[0-9]{3})*,[0-9]{1,4})\s*(?:por|/)\s*(?:cota|ação|acao))|(?:(?:por|/)\s*(?:cota|ação|acao)\s*(?:de|:)?\s*R\$\s*([0-9]{1,4}(?:\.[0-9]{3})*,[0-9]{1,4}))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PerUnitAmount();

    [GeneratedRegex(@"\b(?:data\s+(?:de|do)\s+pagamento|pagamento|pagos?)(?:\s+(?:será|sera)\s+(?:realizado|efetuado))?\s*(?::|em|no\s+dia|no|para|previsto\s+para)?\s*(\d{1,2}[/-]\d{1,2}[/-]\d{4})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PaymentDatePattern();

    public static IReadOnlyList<DividendForecastItem> Calculate(IEnumerable<PortfolioPosition> positions, IEnumerable<InvestmentEvent> events, DateTimeOffset now, DateOnly referenceMonth)
    {
        var recentEvents = events.Where(x => x.CreatedAt <= now)
            .SelectMany(x => x.AffectedAssets.Select(asset => (asset.Id, Event: x)))
            .Where(x => x.Event.DocumentAnalysis?.Document is { } document && document.PublishedAt <= now && document.PublishedAt >= now.AddDays(-90))
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
                var announcements = stockEvents.Select(x => (Event: x, Notice: StockDividendNoticeParser.Parse(string.Join("\n", x.Facts.Append(x.Summary)))))
                    .Where(x => x.Notice is not null && x.Notice.PaymentDate.Year == referenceMonth.Year && x.Notice.PaymentDate.Month == referenceMonth.Month).ToList();
                if (announcements.Count > 0)
                {
                    var latest = announcements.GroupBy(x => x.Event.DocumentAnalysis!.DocumentId).First();
                    var notices = latest.Select(x => x.Notice!).Distinct().ToList();
                    if (notices.Count == 1)
                    {
                        var document = latest.First().Event.DocumentAnalysis!.Document!;
                        result.Add(new(position.AssetId, position.Asset.Ticker, decimal.Round(notices[0].AmountPerShare * position.Quantity, 2),
                            "Documento analisado", document.SourceUrl, document.PublishedAt, notices[0].PaymentDate));
                        continue;
                    }
                }
            }

            result.Add(new(position.AssetId, position.Asset.Ticker, null, "Sem pagamento informado neste mês", null, null));
        }
        return result.OrderBy(x => x.PaymentDate is null).ThenBy(x => x.PaymentDate)
            .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.AssetId).ToList();
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
