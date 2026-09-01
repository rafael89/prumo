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
}
