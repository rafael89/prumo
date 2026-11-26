using System.Globalization;
using System.Text;
using Prumo.Domain.Enums;

namespace Prumo.Application.Documents;

public static class DocumentClassificationPolicy
{
    public static DocumentType Map(string? sourceCategory, string? sourceType, string? sourceSpecies)
    {
        var category = Normalize(sourceCategory);
        var type = Normalize(sourceType);

        if (category is "FATOS RELEVANTES" or "FATO RELEVANTE" || type is "FATO RELEVANTE" or "FATOS RELEVANTES" or "FATO RELEV")
            return DocumentType.RelevantFact;
        if (category is "COMUNICADO AO MERCADO" or "COMUNICADOS AO MERCADO" || type is "COMUNICADO AO MERCADO" or "COMUNICADO")
            return DocumentType.Communication;
        if (category is "RELATORIOS" && (type is "RELATORIO GERENCIAL" or "RELAT GERENCIAL"))
            return DocumentType.ManagementReport;
        if (category is "INFORMES PERIODICOS" && type is "INFORME MENSAL")
            return DocumentType.MonthlyReport;
        if (category is "INFORMES PERIODICOS" && type is "INFORME TRIMESTRAL")
            return DocumentType.QuarterlyReport;

        // Compatibility with older CVM eventual files that expose only TP_DOC.
        if (type is "RELATORIO GERENCIAL" or "RELAT GERENCIAL" or "RELATORIO DE GESTAO") return DocumentType.ManagementReport;
        if (type is "INFORME MENSAL") return DocumentType.MonthlyReport;
        if (type is "INFORME TRIMESTRAL") return DocumentType.QuarterlyReport;
        return DocumentType.Other;
    }

    // These CVM/FNET filings describe operational steps of a public offering.
    // They are useful as an audit trail, but normally repeat the material fact
    // that should feed the Radar. Keep the metadata record and skip the PDF/AI
    // pipeline for them.
    public static bool IsOperationalIssuanceDocument(string? sourceCategory, string? sourceType, string? sourceSpecies)
    {
        var values = new[] { sourceCategory, sourceType, sourceSpecies }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .ToArray();

        return values.Any(value =>
            value.Contains("ANUNCIO DE INICIO", StringComparison.Ordinal)
            || value.Contains("ANUNCIO DE ENCERRAMENTO", StringComparison.Ordinal)
            || value.Contains("EMISSAO DE COTAS", StringComparison.Ordinal)
            || value.Contains("EMISSAO DE NOVAS COTAS", StringComparison.Ordinal)
            || value.Contains("PROSPECTO", StringComparison.Ordinal)
            || value.Contains("LAMINA DA OFERTA", StringComparison.Ordinal)
            || value.Contains("LAMINA DE OFERTA", StringComparison.Ordinal)
            || value.Contains("MATERIAL PUBLICITARIO", StringComparison.Ordinal)
            || value.Contains("MATERIAL DA OFERTA", StringComparison.Ordinal)
            || value.Contains("BOLETIM DE SUBSCRICAO", StringComparison.Ordinal)
            || value.Contains("AVISO AO MERCADO", StringComparison.Ordinal));
    }

    public static bool IsShareholderNotice(string? sourceCategory, string? sourceType, string? sourceSpecies)
        => new[] { sourceCategory, sourceType, sourceSpecies }.Select(Normalize)
            .Any(value => value is "AVISO AOS ACIONISTAS" or "AVISOS AOS ACIONISTAS");

    public static bool IsPeriodicReport(string? sourceCategory, string? sourceType)
        => Normalize(sourceCategory) is "RELATORIOS" or "INFORMES PERIODICOS"
            || Map(sourceCategory, sourceType, null) is DocumentType.ManagementReport or DocumentType.MonthlyReport or DocumentType.QuarterlyReport;

    public static bool MayReportDistribution(AssetClass assetClass, DocumentType documentType, string? category, string? type, string? species)
        => assetClass switch
        {
            AssetClass.Fii => FiiDistributionNoticeParser.IsNotice(category, type),
            AssetClass.BrazilianStock => IsShareholderNotice(category, type, species)
                || documentType == DocumentType.RelevantFact,
            _ => false
        };

    public static string? GetAutomaticAnalysisIgnoredReason(string? sourceCategory, string? sourceType, string? sourceSpecies)
    {
        if (Map(sourceCategory, sourceType, sourceSpecies) == DocumentType.RelevantFact) return null;
        if (IsOperationalIssuanceDocument(sourceCategory, sourceType, sourceSpecies))
            return "Documento operacional de emissão; mantido apenas como histórico e não enviado para análise.";

        var values = new[] { sourceCategory, sourceType, sourceSpecies }.Select(Normalize);
        if (values.Any(value => value is "MAPA FINAL DE VOTACAO DETALHADO"
            or "MAPA FINAL DE VOTACAO SINTETICO"
            or "MAPA CONSOLIDADO DE VOTO A DISTANCIA"
            or "BOLETIM DE VOTO A DISTANCIA"))
            return "Documento operacional de votação; mantido apenas como histórico e não enviado para análise.";

        return null;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var withoutAccents = new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return string.Join(' ', withoutAccents.ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
