namespace Prumo.Infrastructure.DocumentSources;

public sealed class CvmFnetOptions
{
    public string DocumentosEventuaisUrlTemplate { get; set; } = "https://dados.cvm.gov.br/dados/FI/DOC/EVENTUAL/DADOS/eventual_fi_{0}.csv";
    public string CompanhiasIpeUrlTemplate { get; set; } = "https://dados.cvm.gov.br/dados/CIA_ABERTA/DOC/IPE/DADOS/ipe_cia_aberta_{0}.zip";
    public string InformeMensalFiiUrlTemplate { get; set; } = "https://dados.cvm.gov.br/dados/FII/DOC/INF_MENSAL/DADOS/inf_mensal_fii_{0}.zip";
    public string CadastroFiiUrl { get; set; } = "https://dados.cvm.gov.br/dados/fi/cad/DADOS/registro_fundo_classe.zip";
    public bool FnetDirectDiscoveryEnabled { get; set; } = true;
    public string FnetManagerUrl { get; set; } = "https://fnet.bmfbovespa.com.br/fnet/publico/abrirGerenciadorDocumentosCVM";
    public string FnetDocumentsUrl { get; set; } = "https://fnet.bmfbovespa.com.br/fnet/publico/pesquisarGerenciadorDocumentosDados";
    public int FnetDirectLookbackDays { get; set; } = 14;
    public bool EnetDirectDiscoveryEnabled { get; set; } = true;
    public string EnetConsultaUrl { get; set; } = "https://www.rad.cvm.gov.br/ENETWeb/frmConsultaExternaCVM.aspx";
    public int EnetDirectLookbackDays { get; set; } = 14;
    public Dictionary<string, string> CnpjPorTicker { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
