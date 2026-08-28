using System.Globalization;
using System.Text.Json;
using Lodira.Application.Abstractions;

namespace Lodira.Application.Documents;

public static class DocumentAnalysisPrompt
{
    public const string Version = "document-analysis-v1";
    public const string GlobalVersion = "document-analysis-global-v1";

    public static string BuildGlobal(string documentText, PortfolioAnalysisContext context)
    {
        var asset = JsonSerializer.Serialize(new { ticker = context.Ticker, type = context.AssetType, sector = context.Sector });
        return $"""
            Analise o documento exclusivamente para identificar seu significado para o ativo.
            Nao use nem invente contexto de carteira, patrimonio, peso, posicoes ou recomendacoes personalizadas.
            Retorne somente JSON valido com importance, classification, requiresAction, suggestedAction,
            summary, impact, affectedAssets e confidence.
            CONTEXTO DO ATIVO: {asset}
            DOCUMENTO: {documentText}
            """;
    }

    public static string Build(string documentText, PortfolioAnalysisContext context)
    {
        var serializedContext = JsonSerializer.Serialize(new
        {
            portfolio = new
            {
                id = context.PortfolioId,
                name = context.PortfolioName,
                totalValue = FormatCurrency(context.TotalPortfolioValue),
                positionCount = context.PositionCount,
                topPositions = context.TopPositions.Take(PortfolioAnalysisContext.MaxTopPositions).Select(position => new
                {
                    ticker = position.Ticker,
                    type = position.AssetType,
                    weight = FormatPercentage(position.PortfolioWeight)
                }),
                concentration = new
                {
                    largestPosition = FormatPercentage(context.LargestPositionWeight),
                    topThreePositions = FormatPercentage(context.TopThreePositionWeight)
                }
            },
            asset = new
            {
                ticker = context.Ticker,
                type = context.AssetType,
                sector = context.Sector,
                quantity = context.Quantity,
                currentPrice = FormatNullableCurrency(context.CurrentPrice),
                currentValue = FormatNullableCurrency(context.CurrentValue),
                portfolioWeight = FormatPercentage(context.PortfolioWeight)
            },
            position = new
            {
                quantity = context.Quantity,
                averagePrice = FormatCurrency(context.AveragePrice)
            }
        });

        return $"""
            Você analisa exclusivamente o documento abaixo e o contexto estruturado da carteira.
            Use somente as informacoes recebidas. Nao invente valores, datas, percentuais,
            quantidades, eventos, decisoes ou dados externos. A IA nao pode alterar dados
            financeiros; apenas interpretar os valores fornecidos pelo backend.
            Nao recalcule pesos ou valores financeiros. Nao assuma posicoes que nao estejam
            no contexto e nao recomende compra ou venda automaticamente. Diferencie impacto
            no ativo de impacto na carteira e declare quando nao houver dados suficientes.
            Se nao houver informacoes suficientes, use exatamente:
            "Nao ha informacoes suficientes para concluir."
            Retorne somente JSON valido com os campos:
            importance, classification, requiresAction, suggestedAction, summary, impact,
            portfolioImpact, affectedAssets e confidence.

            CONTEXTO DA CARTEIRA:
            {serializedContext}

            DOCUMENTO:
            {documentText}
            """;
    }

    private static string FormatPercentage(decimal value)
    {
        var rounded = decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        return $"{rounded.ToString("F2", CultureInfo.GetCultureInfo("pt-BR"))}%";
    }

    private static string FormatCurrency(decimal value) => value.ToString("C2", CultureInfo.GetCultureInfo("pt-BR"));

    private static string FormatNullableCurrency(decimal? value) => value.HasValue ? FormatCurrency(value.Value) : "Indisponivel";
}
