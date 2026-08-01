using System.Text;

namespace PublicationSite.Api.Data;

/// <summary>
/// Builds the stand-in files the demo dataset uploads: ethics forms and research papers.
///
/// They are real PDFs rather than renamed text, because the point of the demo data is that every
/// screen behaves as it would with genuine content: a reviewer clicks through to a document and a
/// document opens. A file that downloaded and then failed to open would send whoever is testing off
/// hunting for a bug that is not there.
/// </summary>
public static class DemoDocuments
{
    public static byte[] Pdf(string title, string body)
    {
        // Objects are written in order and their byte offsets recorded as we go, because the
        // cross-reference table at the end has to point at each one exactly. A PDF without a
        // correct xref is one most viewers will repair and some will simply reject.
        var output = new MemoryStream();
        var offsets = new List<int>();

        void Write(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            output.Write(bytes, 0, bytes.Length);
        }

        void BeginObject(string content)
        {
            offsets.Add((int)output.Length);
            Write($"{offsets.Count} 0 obj\n{content}\nendobj\n");
        }

        var stream = $"""
            BT /F1 16 Tf 60 780 Td ({Escape(title)}) Tj ET
            BT /F1 11 Tf 60 750 Td ({Escape(body)}) Tj ET
            BT /F1 9 Tf 60 60 Td (Sample document generated for the demonstration dataset.) Tj ET
            """;

        Write("%PDF-1.4\n");
        BeginObject("<< /Type /Catalog /Pages 2 0 R >>");
        BeginObject("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        BeginObject("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] "
                    + "/Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>");
        BeginObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        BeginObject($"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}\nendstream");

        var startXref = (int)output.Length;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write($"{offset:D10} 00000 n \n");
        }

        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{startXref}\n%%EOF");

        return output.ToArray();
    }

    /// <summary>
    /// Brackets and backslashes delimit and escape strings in PDF syntax, so a title containing
    /// one would otherwise end the string early and corrupt everything after it.
    /// </summary>
    private static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
