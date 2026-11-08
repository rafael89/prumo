using Prumo.Application.Radar;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class RadarCardGroupingTests
{
    private static readonly Guid AssetId = Guid.NewGuid();
    private static readonly DateTimeOffset PublishedAt = new(2026, 11, 5, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Engie_incorporation_disclosures_share_a_card_across_document_types()
    {
        var sources = new[]
        {
            Source("Convocação de Assembleia Geral Extraordinária para 30/11/2026 para deliberar sobre a incorporação da ENGIE Brasil Energia Comercializadora Ltda. e atualização do Estatuto Social."),
            Source("Convocação de Assembleia Geral Extraordinária para deliberar sobre a incorporação da ENGIE Brasil Energia Comercializadora Ltda., aprovação do laudo de avaliação contábil e consolidação do estatuto social."),
            Source("Emissão de laudo de avaliação do patrimônio líquido contábil da subsidiária Engie Brasil Energia Comercializadora Ltda. em 31 de agosto de 2026, no valor de R$ 34.242.178,74 após ajustes, para fins de incorporação pela Engie Brasil Energia S.A.", InvestmentEventType.AssetTransaction),
            Source("Aprovação pelo Conselho de Administração da incorporação da ENGIE Brasil Energia Comercializadora Ltda. e convocação de AGE para 30 de novembro de 2026."),
            Source("O Conselho Fiscal emitiu parecer favorável à incorporação da ENGIE Brasil Energia Comercializadora Ltda. (EBC) pela ENGIE Brasil Energia S.A., após aprovação pelo Conselho de Administração, com envio da matéria para deliberação em Assembleia Geral."),
            Source("O Conselho de Administração aprovou a incorporação da controlada integral ENGIE Brasil Energia Comercializadora Ltda e a convocação de Assembleia Geral Extraordinária para 30 de novembro de 2026.")
        };

        var cards = RadarCardGrouping.Build(sources);

        Assert.Single(cards.Values.Distinct());
        Assert.Equal(cards, RadarCardGrouping.Build(sources.Reverse()));
    }

    [Fact]
    public void Bbse_repeated_board_disclosures_group_but_other_decisions_are_separate()
    {
        var resignation = Source("Renúncia de membro do Conselho de Administração e eleição de novo membro para o Comitê de Transações com Partes Relacionadas.");
        var repeated = Source("Renúncia de integrante do Conselho de Administração e eleição de membro para o Comitê de Transações com Partes Relacionadas.");
        var election = Source("Abertura de chamamento para indicação de candidatos ao Conselho de Administração e previsão de AGE para eleição em separado em decorrência de renúncia.");
        var guidance = Source("Revisão do guidance de prêmios emitidos da Brasilseg para o exercício de 2026.", InvestmentEventType.FinancialResult);
        var cards = RadarCardGrouping.Build([resignation, repeated, election, guidance]);
        Assert.Equal(cards[resignation.AnalysisId], cards[repeated.AnalysisId]);
        Assert.NotEqual(cards[resignation.AnalysisId], cards[election.AnalysisId]);
        Assert.NotEqual(cards[resignation.AnalysisId], cards[guidance.AnalysisId]);
    }

    [Theory]
    [InlineData(InvestmentEventType.Distribution)]
    [InlineData(InvestmentEventType.CapitalRaise)]
    [InlineData(InvestmentEventType.Liquidation)]
    public void Recurring_or_capital_events_are_not_merged_by_text(InvestmentEventType type)
    {
        var first = Source("Aprovação do pagamento de dividendos aos acionistas com posição registrada na data de corte.", type);
        var second = first with { AnalysisId = Guid.NewGuid() };
        Assert.Equal(2, RadarCardGrouping.Build([first, second]).Values.Distinct().Count());
    }

    [Fact]
    public void Other_assets_distant_disclosures_different_periods_and_amounts_stay_separate()
    {
        var first = Source("Divulgação dos resultados financeiros trimestrais com crescimento das receitas e lucro recorrente no 3T2026.", InvestmentEventType.FinancialResult);
        AssertSeparate(first, first with { AssetId = Guid.NewGuid() });
        AssertSeparate(first, first with { PublishedAt = PublishedAt.AddDays(8) });
        AssertSeparate(first, first with { Summary = first.Summary.Replace("3T2026", "2T2026") });
        var transaction = Source("Aprovação da aquisição do imóvel comercial localizado na cidade de São Paulo pelo valor de R$ 100.000.000.", InvestmentEventType.AssetTransaction);
        AssertSeparate(transaction, transaction with { Summary = transaction.Summary.Replace("100.000.000", "200.000.000") });
        var incorporation = Source("Aprovação da incorporação da ENGIE Brasil Energia Comercializadora Ltda. e convocação de assembleia extraordinária.");
        AssertSeparate(incorporation, incorporation with { Summary = incorporation.Summary.Replace("Comercializadora", "Distribuidora") });
        AssertSeparate(first with { AssetType = AssetType.Fii }, first with { AssetType = AssetType.Fii });
    }

    private static void AssertSeparate(RadarCardSource first, RadarCardSource second)
        => Assert.Equal(2, RadarCardGrouping.Build([first, second with { AnalysisId = Guid.NewGuid() }]).Values.Distinct().Count());

    private static RadarCardSource Source(string summary, InvestmentEventType type = InvestmentEventType.Governance)
        => new(Guid.NewGuid(), AssetId, AssetType.Stock, PublishedAt, type, summary);
}
