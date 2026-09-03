using System.Text;

namespace Prumo.Domain.Tests;

internal static class PdfTestData
{
    public static MemoryStream CreatePdfStream(string text)
    {
        var escaped = EscapePdfString(text);
        var content = $"BT /F1 12 Tf 72 720 Td ({escaped}) Tj ET";
        var pdf = $$"""
            %PDF-1.4
            1 0 obj
            << /Type /Catalog /Pages 2 0 R >>
            endobj
            2 0 obj
            << /Type /Pages /Kids [3 0 R] /Count 1 >>
            endobj
            3 0 obj
            << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>
            endobj
            4 0 obj
            << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>
            endobj
            5 0 obj
            << /Length {{content.Length}} >>
            stream
            {{content}}
            endstream
            endobj
            xref
            0 6
            0000000000 65535 f 
            0000000010 00000 n 
            0000000063 00000 n 
            0000000122 00000 n 
            0000000248 00000 n 
            0000000318 00000 n 
            trailer
            << /Root 1 0 R /Size 6 >>
            startxref
            409
            %%EOF
            """;

        return new MemoryStream(Encoding.ASCII.GetBytes(pdf.Replace("\r\n", "\n")));
    }

    public static MemoryStream CreateInvalidPdfStream()
    {
        return new MemoryStream(Encoding.UTF8.GetBytes("isso nao e um pdf valido"));
    }

    private static string EscapePdfString(string text)
    {
        return text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
    }
}
