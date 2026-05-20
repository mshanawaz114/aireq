// TailoredResumeRenderer — renders a ResumeContent into a clean one/two-column
// PDF via QuestPDF. Pure (content -> bytes), so it's unit-testable without I/O.
//
// QuestPDF Community license is set once at worker startup (Program.cs).
//
// Refs: AIRMVP1-302

using Aireq.Shared.Contracts;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Aireq.Worker.Tailoring;

public static class TailoredResumeRenderer
{
    public static byte[] Render(ResumeContent r)
    {
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(36);
                page.Size(PageSizes.Letter);
                page.DefaultTextStyle(t => t.FontSize(10).FontColor(Colors.Grey.Darken4));

                page.Header().Column(col =>
                {
                    col.Item().Text(r.FullName ?? "Consultant").FontSize(20).SemiBold();
                    if (!string.IsNullOrWhiteSpace(r.Headline))
                        col.Item().Text(r.Headline!).FontSize(11).FontColor(Colors.Blue.Darken2);
                    var contact = string.Join("  •  ", new[] { r.Location, r.Email, r.Phone }
                        .Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (contact.Length > 0)
                        col.Item().Text(contact).FontSize(9).FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(12);

                    if (!string.IsNullOrWhiteSpace(r.Summary))
                        Section(col, "Summary", s => s.Item().Text(r.Summary!));

                    if (r.Skills.Count > 0)
                        Section(col, "Skills", s =>
                            s.Item().Text(string.Join(", ", r.Skills.Select(sk => sk.Name))));

                    if (r.Experiences.Count > 0)
                        Section(col, "Experience", s =>
                        {
                            foreach (var exp in r.Experiences)
                            {
                                s.Item().PaddingTop(4).Row(row =>
                                {
                                    row.RelativeItem().Text($"{exp.Title} — {exp.Company}").SemiBold();
                                    row.ConstantItem(120).AlignRight().Text(DateRange(exp.StartDate, exp.EndDate))
                                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                                });
                                foreach (var bullet in exp.Bullets)
                                    s.Item().PaddingLeft(10).Text($"• {bullet}");
                            }
                        });

                    if (r.Educations.Count > 0)
                        Section(col, "Education", s =>
                        {
                            foreach (var ed in r.Educations)
                            {
                                var line = string.Join(", ",
                                    new[] { ed.Degree, ed.Field, ed.School }.Where(x => !string.IsNullOrWhiteSpace(x)));
                                s.Item().Text(line + (ed.EndDate is { } d ? $" ({d})" : ""));
                            }
                        });

                    if (r.Certifications.Count > 0)
                        Section(col, "Certifications", s =>
                            s.Item().Text(string.Join(", ", r.Certifications)));
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Tailored by Aireq").FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf();
    }

    private static void Section(ColumnDescriptor col, string title, Action<ColumnDescriptor> body)
    {
        col.Item().Column(s =>
        {
            s.Item().Text(title.ToUpperInvariant()).FontSize(11).SemiBold().FontColor(Colors.Blue.Darken2);
            s.Item().PaddingBottom(2).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
            body(s);
        });
    }

    private static string DateRange(string? start, string? end) =>
        (start, end) switch
        {
            (null or "", null or "") => "",
            (null or "", var e) => e!,
            (var s, null or "") => $"{s} – present",
            var (s, e) => $"{s} – {e}",
        };
}
