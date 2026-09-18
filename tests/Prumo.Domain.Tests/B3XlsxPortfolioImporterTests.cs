using System.IO.Compression;
using System.Text;
using Prumo.Domain.Enums;
using Prumo.Infrastructure.PortfolioImports;

namespace Prumo.Domain.Tests;

public sealed class B3XlsxPortfolioImporterTests
{
    [Fact]
    public async Task Parses_sanitized_real_layout_for_stocks_and_fiis()
    {
        await using var stream = Fixture();
        var result = await new B3XlsxPortfolioImporter().ParseAsync(stream);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(("ABCD3", AssetType.Stock), (result.Items[0].Ticker, result.Items[0].AssetType));
        Assert.Equal(AssetClass.BrazilianStock, result.Items[0].AssetClass);
        Assert.Equal(AssetClass.Fii, result.Items[1].AssetClass);
        Assert.Equal(123.5m, result.Items[1].Quantity);
        Assert.Equal(9876.54m, result.Items[1].CurrentValue);
        Assert.Equal("Corretora Sanitizada", result.Items[0].Institution);
        Assert.Equal("12345678000190", result.Items[0].Cnpj);
        Assert.Equal("98765432000110", result.Items[1].Cnpj);
    }

    [Fact]
    public async Task Parses_negotiations_as_imported_buy_references()
    {
        await using var stream = Fixture();
        var result = await new B3XlsxPortfolioImporter().ParseAsync(stream);
        var transaction = Assert.Single(result.Transactions);
        Assert.Equal("XPML11", transaction.Ticker);
        Assert.Equal(new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero), transaction.OccurredAt);
        Assert.Equal(44m, transaction.Quantity);
        Assert.Equal(105.64m, transaction.UnitPrice);
        Assert.Equal(4648.16m, transaction.TotalAmount);
    }

    [Fact]
    public async Task Ignores_rights_and_warns_about_tesouro()
    {
        await using var stream = Fixture(includeRight: true);
        var result = await new B3XlsxPortfolioImporter().ParseAsync(stream);
        Assert.DoesNotContain(result.Items, item => item.Ticker == "ABCD12");
        Assert.Contains(result.Warnings, warning => warning.Contains("Direito", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("Tesouro", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Rejects_formula_cells_without_executing_them()
    {
        await using var stream = Fixture(includeFormula: true);
        var result = await new B3XlsxPortfolioImporter().ParseAsync(stream);
        Assert.Contains(result.Errors, error => error.Contains("fórmulas", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Rejects_missing_expected_header()
    {
        await using var stream = Fixture(invalidHeader: true);
        var result = await new B3XlsxPortfolioImporter().ParseAsync(stream);
        Assert.Contains(result.Errors, error => error.Contains("cabeçalho", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("0", "0", "10", "30", 0, 1)]
    [InlineData("", "", "10", "30", 0, 1)]
    [InlineData("2", "20", "0", "0", 1, 0)]
    [InlineData("2", "20", "10", "30", 1, 1)]
    [InlineData("0", "0", "0", "0", 0, 0)]
    public async Task Purchases_and_sales_are_parsed_independently(string buyQuantity, string buyPrice, string sellQuantity, string sellPrice, int buys, int sells)
    {
        await using var stream = Fixture(buyQuantity: buyQuantity, buyPrice: buyPrice, sellQuantity: sellQuantity, sellPrice: sellPrice);
        var result = await new B3XlsxPortfolioImporter().ParseAsync(stream);
        Assert.Empty(result.Errors);
        Assert.Equal(buys, result.Transactions.Count(x => x.Type == PortfolioTransactionType.Buy));
        Assert.Equal(sells, result.Transactions.Count(x => x.Type == PortfolioTransactionType.Sell));
        if (sells > 0)
        {
            var sale = result.Transactions.Single(x => x.Type == PortfolioTransactionType.Sell);
            Assert.Equal(10, sale.Quantity); Assert.Equal(30, sale.UnitPrice); Assert.Equal(300, sale.TotalAmount);
        }
    }

    private static MemoryStream Fixture(bool includeRight = false, bool includeFormula = false, bool invalidHeader = false,
        string buyQuantity = "44", string buyPrice = "105.64", string sellQuantity = "0", string sellPrice = "0")
    {
        var shared = new[] { "Produto", "Instituição", "Conta", "Código de Negociação", "CNPJ", "ISIN", "Tipo", "Escriturador", "Quantidade", "Disponível", "Indisponível", "Motivo", "Preço de Fechamento", "Valor Atualizado", "ABCD3 - Empresa Sanitizada", "Corretora Sanitizada", "ABCD3", "ON", "ABCD12", "Direito", "EFGH11 - Fundo Sanitizado", "EFGH11", "Cotas", "12345678000190", "98765432000110", "Código de Negociação", "Período (Inicial)", "Período (Final)", "Instituição", "Quantidade (Compra)", "Preço Médio (Compra)", "XPML11", "06/07/2026", "Corretora Sanitizada" };
        shared = shared.Concat(new[] { "Quantidade (Venda)", "Preço Médio (Venda)" }).ToArray();
        var sharedXml = "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" + string.Concat(shared.Select(value => $"<si><t>{value}</t></si>")) + "</sst>";
        string Shared(string column, int row, int value) => $"<c r=\"{column}{row}\" t=\"s\"><v>{value}</v></c>";
        string Number(string column, int row, string value) => $"<c r=\"{column}{row}\"><v>{value}</v></c>";
        var headers = invalidHeader ? Shared("A", 1, 14) : string.Concat(Shared("A", 1, 0), Shared("B", 1, 1), Shared("D", 1, 3), Shared("G", 1, 6), Shared("I", 1, 8), Shared("M", 1, 12), Shared("N", 1, 13));
        var action = $"<row r=\"1\">{headers}</row><row r=\"2\">{Shared("A", 2, 14)}{Shared("B", 2, 15)}{Shared("D", 2, 16)}{Shared("E", 2, 23)}{Shared("G", 2, 17)}{Number("I", 2, "10")}{Number("M", 2, "12.5")}{Number("N", 2, "125")}{(includeFormula ? "<c r=\"O2\"><f>1+1</f><v>2</v></c>" : "")}</row>";
        var fii = $"<row r=\"1\">{headers}</row><row r=\"2\">{Shared("A", 2, 20)}{Shared("B", 2, 15)}{Shared("D", 2, 21)}{Shared("E", 2, 24)}{Shared("G", 2, 22)}{Number("I", 2, "123.5")}{Number("M", 2, "80")}{Number("N", 2, "9876.54")}</row>";
        var right = includeRight ? $"<row r=\"3\">{Shared("A", 3, 18)}{Shared("B", 3, 15)}{Shared("D", 3, 18)}{Shared("G", 3, 19)}{Number("I", 3, "2")}{Number("M", 3, "1")}{Number("N", 3, "2")}</row>" : "";
        var sheet = (string rows) => $"<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>{rows}</sheetData></worksheet>";
        var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            void Add(string name, string content) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8); writer.Write(content); }
            var negotiations = $"<row r=\"1\">{Shared("A", 1, 25)}{Shared("B", 1, 26)}{Shared("C", 1, 27)}{Shared("D", 1, 28)}{Shared("E", 1, 29)}{Shared("H", 1, 30)}{Shared("J", 1, 34)}{Shared("K", 1, 35)}</row><row r=\"2\">{Shared("A", 2, 31)}{Shared("B", 2, 32)}{Shared("D", 2, 33)}{Number("E", 2, buyQuantity)}{Number("H", 2, buyPrice)}{Number("J", 2, sellQuantity)}{Number("K", 2, sellPrice)}</row>";
            Add("xl/sharedStrings.xml", sharedXml); Add("xl/worksheets/sheet1.xml", sheet(action)); Add("xl/worksheets/sheet2.xml", sheet(fii + right)); Add("xl/worksheets/sheet3.xml", sheet("")); Add("xl/worksheets/sheet5.xml", sheet(negotiations));
        }
        output.Position = 0; return output;
    }

}
