namespace Lodira.Infrastructure.DocumentSources;

public sealed class CvmFnetOptions
{
    public string DocumentosEventuaisUrlTemplate { get; set; } = "https://dados.cvm.gov.br/dados/FI/DOC/EVENTUAL/DADOS/eventual_fi_{0}.csv";
    public Dictionary<string, string> CnpjPorTicker { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
