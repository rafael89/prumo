using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using System.Xml;
using Prumo.Application.PortfolioImports;
using Prumo.Domain.Enums;

namespace Prumo.Infrastructure.PortfolioImports;

public sealed class B3XlsxPortfolioImporter : IPortfolioImporter
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const long MaxFileBytes = 10 * 1024 * 1024;
    private const long MaxExpandedBytes = 50 * 1024 * 1024;

    public bool CanHandle(string fileName, string? contentType, ReadOnlySpan<byte> header) =>
        Path.GetExtension(fileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) && header.StartsWith("PK"u8);

    public Task<NormalizedPortfolioImport> ParseAsync(Stream content, CancellationToken cancellationToken = default)
    {
        try
        {
            if (content.Length > MaxFileBytes) return Task.FromResult(Failure("O arquivo excede o limite de 10 MB."));
            using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Sum(entry => entry.Length) > MaxExpandedBytes) return Task.FromResult(Failure("O conteúdo descompactado excede o limite permitido."));
            var shared = ReadSharedStrings(zip);
            var items = new List<NormalizedPortfolioImportItem>();
            var transactions = new List<NormalizedPortfolioImportTransaction>();
            var warnings = new List<string>();
            for (var sheet = 1; sheet <= 2; sheet++)
            {
                var entry = zip.GetEntry($"xl/worksheets/sheet{sheet}.xml");
                if (entry is null) return Task.FromResult(Failure("O arquivo não contém as abas de posições esperadas."));
                var xml = XDocument.Load(entry.Open());
                if (xml.Descendants(Main + "f").Any()) return Task.FromResult(Failure("Arquivos com fórmulas não são aceitos."));
                var rows = xml.Descendants(Main + "row").ToList();
                if (rows.Count == 0 || !HasExpectedHeaders(rows[0], shared)) return Task.FromResult(Failure("A aba de posições não possui o cabeçalho esperado."));
                foreach (var row in rows.Skip(1))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var cells = row.Elements(Main + "c").ToDictionary(c => Column(c.Attribute("r")?.Value ?? ""), c => Value(c, shared));
                    if (!cells.TryGetValue("D", out var ticker) || !System.Text.RegularExpressions.Regex.IsMatch(ticker, "^[A-Z]{4}[0-9]{1,2}$")) continue;
                    if (string.Equals(cells.GetValueOrDefault("G"), "Direito", StringComparison.OrdinalIgnoreCase)) { warnings.Add($"Direito {ticker} ignorado: não é uma posição de ativo suportada."); continue; }
                    if (!TryDecimal(cells.GetValueOrDefault("I"), out var quantity) || !TryDecimal(cells.GetValueOrDefault("N"), out var value) || !TryDecimal(cells.GetValueOrDefault("M"), out var price))
                        return Task.FromResult(new NormalizedPortfolioImport(PortfolioImportSource.B3File, null, SnapshotCompleteness.Unknown, [], warnings, [$"Valores inválidos para {ticker}."]));
                    items.Add(new(ticker, cells.GetValueOrDefault("A", ticker), sheet == 2 ? AssetType.Fii : AssetType.Stock, sheet == 2 ? AssetClass.Fii : AssetClass.BrazilianStock, quantity, price, value, null, cells.GetValueOrDefault("B"), cells.GetValueOrDefault("E")));
                }
            }
            if (zip.GetEntry("xl/worksheets/sheet3.xml") is null) return Task.FromResult(Failure("O arquivo não contém a aba de Tesouro Direto esperada."));
            warnings.Add("Posições de Tesouro Direto não foram importadas porque não possuem ticker de negociação no modelo atual.");
            var negotiationsEntry = zip.GetEntry("xl/worksheets/sheet5.xml");
            if (negotiationsEntry is not null)
            {
                var negotiations = XDocument.Load(negotiationsEntry.Open());
                if (negotiations.Descendants(Main + "f").Any()) return Task.FromResult(Failure("Arquivos com fórmulas não são aceitos."));
                var rows = negotiations.Descendants(Main + "row").ToList();
                if (rows.Count > 0 && HasNegotiationHeaders(rows[0], shared))
                {
                    foreach (var row in rows.Skip(1))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var cells = Cells(row, shared);
                        var ticker = cells.GetValueOrDefault("A", "").Trim().ToUpperInvariant();
                        if (!System.Text.RegularExpressions.Regex.IsMatch(ticker, "^[A-Z]{4}[0-9]{1,2}$")) continue;
                        if (!TryDecimal(cells.GetValueOrDefault("E"), out var quantity) || quantity <= 0) continue;
                        if (!TryDecimal(cells.GetValueOrDefault("H"), out var unitPrice) || unitPrice <= 0) continue;
                        if (!DateTimeOffset.TryParseExact(cells.GetValueOrDefault("B"), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var occurredAt))
                        {
                            warnings.Add($"Negociação de {ticker} ignorada: período inicial inválido.");
                            continue;
                        }
                        transactions.Add(new(ticker, occurredAt, quantity, unitPrice, decimal.Round(quantity * unitPrice, 2), cells.GetValueOrDefault("D")));
                    }
                }
            }
            var consolidated = items.GroupBy(i => i.Ticker).Select(g => { var first = g.First(); var quantity = g.Sum(i => i.Quantity); return first with { Quantity = quantity, CurrentValue = g.Sum(i => i.CurrentValue ?? 0), CurrentPrice = quantity == 0 ? first.CurrentPrice : g.Sum(i => (i.CurrentPrice ?? 0) * i.Quantity) / quantity }; }).ToList();
            var normalized = new NormalizedPortfolioImport(PortfolioImportSource.B3File, null, SnapshotCompleteness.Partial, consolidated, warnings, consolidated.Count == 0 ? ["Nenhuma posição de ação ou fundo foi encontrada."] : []);
            return Task.FromResult(normalized with { Transactions = transactions });
        }
        catch (InvalidDataException) { return Task.FromResult(Failure("O arquivo XLSX está corrompido ou não é válido.")); }
        catch (XmlException) { return Task.FromResult(Failure("O arquivo XLSX está corrompido ou não é válido.")); }
    }

    private static NormalizedPortfolioImport Failure(string message) => new(PortfolioImportSource.B3File, null, SnapshotCompleteness.Unknown, [], [], [message]);
    private static bool HasExpectedHeaders(XElement row, Dictionary<int, string> shared)
    {
        var cells = row.Elements(Main + "c").ToDictionary(c => Column(c.Attribute("r")?.Value ?? ""), c => Value(c, shared));
        return cells.GetValueOrDefault("A", "").Contains("Produto", StringComparison.OrdinalIgnoreCase) && cells.GetValueOrDefault("B", "").Contains("Institui", StringComparison.OrdinalIgnoreCase) && cells.GetValueOrDefault("D", "").Contains("Negocia", StringComparison.OrdinalIgnoreCase) && cells.GetValueOrDefault("G", "").Contains("Tipo", StringComparison.OrdinalIgnoreCase) && cells.GetValueOrDefault("I", "").Contains("Quantidade", StringComparison.OrdinalIgnoreCase) && cells.GetValueOrDefault("M", "").Contains("Fechamento", StringComparison.OrdinalIgnoreCase) && cells.GetValueOrDefault("N", "").Contains("Atualizado", StringComparison.OrdinalIgnoreCase);
    }
    private static bool HasNegotiationHeaders(XElement row, Dictionary<int, string> shared)
    {
        var cells = Cells(row, shared);
        return cells.GetValueOrDefault("A", "").Contains("Código de Negociação", StringComparison.OrdinalIgnoreCase)
            && cells.GetValueOrDefault("B", "").Contains("Período", StringComparison.OrdinalIgnoreCase)
            && cells.GetValueOrDefault("E", "").Contains("Compra", StringComparison.OrdinalIgnoreCase)
            && cells.GetValueOrDefault("H", "").Contains("Preço Médio", StringComparison.OrdinalIgnoreCase);
    }
    private static Dictionary<string, string> Cells(XElement row, Dictionary<int, string> shared)
        => row.Elements(Main + "c").ToDictionary(c => Column(c.Attribute("r")?.Value ?? ""), c => Value(c, shared));
    private static Dictionary<int, string> ReadSharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml"); if (entry is null) return [];
        var xml = XDocument.Load(entry.Open()); return xml.Descendants(Main + "si").Select((si, i) => (i, Text: string.Concat(si.Descendants(Main + "t").Select(t => t.Value)))).ToDictionary(x => x.i, x => x.Text);
    }
    private static string Value(XElement cell, Dictionary<int, string> shared) { var value = cell.Element(Main + "v")?.Value ?? ""; return cell.Attribute("t")?.Value == "s" && int.TryParse(value, out var index) && shared.TryGetValue(index, out var text) ? text : value; }
    private static string Column(string reference) => new(reference.TakeWhile(char.IsLetter).ToArray());
    private static bool TryDecimal(string? value, out decimal result) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
}
