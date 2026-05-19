// PdfTextExtractor — extract plain text from a PDF byte stream using PdfPig.
//
// Why PdfPig: pure .NET, MIT, no native deps. Good enough for most resumes
// (which are unfortunately a graveyard of weird fonts and table layouts).
// If extraction quality becomes a bottleneck we can swap to an OCR pipeline
// later — that decision lives in AIRMVP1-105's commit, not the interface.
//
// Refs: AIRMVP1-105

using UglyToad.PdfPig;

namespace Aireq.Worker.Resumes;

public static class PdfTextExtractor
{
    /// <summary>
    /// Extract concatenated page text from a PDF stream. Pages are joined with
    /// a double newline so the LLM has visible page breaks (some resumes split
    /// awkwardly across pages).
    /// </summary>
    public static string Extract(Stream pdfStream)
    {
        // PdfPig wants a seekable stream; if the caller handed us a network
        // stream, copy into memory first.
        Stream source;
        if (pdfStream.CanSeek)
        {
            source = pdfStream;
        }
        else
        {
            var ms = new MemoryStream();
            pdfStream.CopyTo(ms);
            ms.Position = 0;
            source = ms;
        }

        using var pdf = PdfDocument.Open(source);
        var pages = pdf.GetPages().Select(p => p.Text);
        return string.Join("\n\n", pages);
    }
}
