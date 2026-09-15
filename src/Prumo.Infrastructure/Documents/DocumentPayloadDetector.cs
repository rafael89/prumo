using System.Xml;
using System.Xml.Linq;

namespace Prumo.Infrastructure.Documents;

public enum DocumentPayloadKind
{
    Pdf,
    Xml
}

public static class DocumentPayloadDetector
{
    public static bool TryDetect(byte[] payload, string? contentType, out DocumentPayloadKind kind)
    {
        if (LooksLikePdf(payload))
        {
            kind = DocumentPayloadKind.Pdf;
            return true;
        }

        if (TryParseXml(payload, out _))
        {
            kind = DocumentPayloadKind.Xml;
            return true;
        }

        kind = default;
        return false;
    }

    public static bool TryParseXml(byte[] payload, out XDocument? document)
    {
        document = null;
        if (payload.Length == 0) return false;

        try
        {
            using var stream = new MemoryStream(payload, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 5_000_000
            });
            document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            return document.Root is not null
                && !string.Equals(document.Root.Name.LocalName, "html", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(document.Root.Name.LocalName, "body", StringComparison.OrdinalIgnoreCase);
        }
        catch (XmlException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool LooksLikePdf(byte[] payload)
    {
        var limit = Math.Min(Math.Max(0, payload.Length - 5), 1024);
        for (var index = 0; index <= limit; index++)
        {
            if (payload[index] == (byte)'%'
                && payload[index + 1] == (byte)'P'
                && payload[index + 2] == (byte)'D'
                && payload[index + 3] == (byte)'F'
                && payload[index + 4] == (byte)'-') return true;
        }
        return false;
    }
}
