using Lodira.Application.Contributions;
using Lodira.Domain.Enums;

namespace Lodira.Domain.Tests;

public sealed class ContributionTextTests
{
    [Fact]
    public void Apresenta_classe_de_acoes_em_portugues()
    {
        Assert.Equal("A\u00e7\u00f5es", ContributionText.AssetClassLabel(AssetClass.BrazilianStock));
    }

    [Fact]
    public void Normaliza_nome_interno_da_classe_em_texto_da_ia()
    {
        var result = ContributionText.NormalizeAssetClassNames("BrazilianStock foi priorizado; FixedIncome ficou em segundo.");

        Assert.Equal("A\u00e7\u00f5es foi priorizado; Renda fixa ficou em segundo.", result);
    }

    [Fact]
    public void Substitui_campos_tecnicos_e_formata_valor_do_resumo()
    {
        var result = ContributionText.NormalizeAssetClassNames(
            "O aporte total de 4000.00 foi integralmente alocado na classe BrazilianStock, que apresenta necessidade com currentClassWeight abaixo de targetClassWeight.");

        Assert.Equal("O aporte total de R$ 4.000,00 foi integralmente alocado na classe A\u00e7\u00f5es, que apresenta necessidade com o peso atual da classe est\u00e1 abaixo da meta.", result);
    }

    [Fact]
    public void Normaliza_explicacao_de_alternativa_sem_termo_tecnico()
    {
        var result = ContributionText.NormalizeAssetClassNames(
            "Foi preterido por outro candidato eleg\u00edvel da mesma classe com maior benef\u00edcio marginal.");

        Assert.Equal("Foi preterido por outro ativo eleg\u00edvel da mesma classe com maior benef\u00edcio marginal.", result);
    }
}
