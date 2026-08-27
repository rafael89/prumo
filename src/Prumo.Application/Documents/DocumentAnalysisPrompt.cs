using System.Text.Json;
using Prumo.Application.Abstractions;

namespace Prumo.Application.Documents;

public static class DocumentAnalysisPrompt
{
    public const string Version = "document-analysis-v1";

    public static string Build(string documentText, PortfolioAnalysisContext context)
    {
        var serializedContext = JsonSerializer.Serialize(new
        {
            asset = new
            {
                ticker = context.Ticker,
                type = context.AssetType
            },
            position = new
            {
                quantity = context.Quantity,
                averagePrice = context.AveragePrice,
                currentValue = context.CurrentValue,
                portfolioWeight = context.PortfolioWeight
            },
            sector = context.Sector
        });

        return $"""
            Você analisa exclusivamente o documento abaixo e o contexto estruturado da carteira.
            Use somente as informacoes recebidas. Nao invente valores, datas, percentuais,
            quantidades, eventos, decisoes ou dados externos. A IA nao pode alterar dados
            financeiros; apenas interpretar os valores fornecidos pelo backend.
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
}
