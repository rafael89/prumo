using System.Globalization;
using System.Text;
using Lodira.Domain.Enums;

namespace Lodira.Application.Documents;

public static class DocumentClassificationPolicy
{
    public static DocumentType Map(string? sourceCategory, string? sourceType, string? sourceSpecies)
    {
        var category = Normalize(sourceCategory);
        var type = Normalize(sourceType);

        if (category is "FATOS RELEVANTES" or "FATO RELEVANTE" || type is "FATO RELEVANTE" or "FATOS RELEVANTES" or "FATO RELEV")
            return DocumentType.RelevantFact;
        if (type is "COMUNICADO AO MERCADO" or "COMUNICADO")
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

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var withoutAccents = new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return string.Join(' ', withoutAccents.ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
